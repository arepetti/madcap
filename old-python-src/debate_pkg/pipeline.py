"""
DebatePipeline — the per-question state machine.

This is the single place that decides what happens when the user asks a
question. The chat loop just hands a line to `DebatePipeline.run()` and
gets out of the way. The actors themselves are kept dumb — they only
know how to render their system prompt and how to take a turn.

Phases (one full round = one call to `run`):
  0. Reset per-round actors so their system prompts pick up the latest
     `prior_rephrased` history and answerer profile.
  1. Rephrase the user's question via the Judge, looping on CLARIFY: /
     REPHRASED: with a bounded number of "use the protocol tokens" nudges.
  2. Debate the rephrased question with the Judge acting as a one-way
     channel constraint: each Answerer reply is restated by the Judge as
     neutral facts before the Critic sees it. The Critic only ever sees
     those restatements. The Answerer hears critiques raw (the constraint
     is one-way). The Judge issues a verdict at the end, driven by an
     explicit prompt.
  3. Bookkeeping (append the rephrased question to the session history)
     and Phase 3 profile-note extraction.

To fine-tune the process, edit this file. Override any of the protected
methods in a subclass to swap behaviour without touching the actors or
the chat loop.
"""

import re
import time

from .personas import render_answerer_profile
from .profile import is_stylistic
from .ui import (
    print_answerer,
    print_critic,
    print_final_answer,
    print_judge_message,
    print_restatement,
    print_warning,
    prompt_line,
)


# Prompt text used outside the team debate. Kept at module scope so they're
# easy to find and tweak.

NUDGE_PROMPT = (
    "Please respond with either 'CLARIFY: <question>' or "
    "'REPHRASED: <neutral version of the question>'."
)

RESTATE_PROMPT_TEMPLATE = (
    "Restate the following Answerer reply as neutral facts. "
    "Strip rhetorical flourishes, hedging, and persuasive framing. "
    "Do not add your own opinion, do not correct, do not contradict, "
    "do not introduce new information. Simply re-express the substantive "
    "claims in plain, neutral language. If a claim is uncertain in the "
    "original, say so factually (\"the Answerer claims X but does not "
    "provide evidence\").\n\n"
    "ANSWERER REPLY:\n{answer}"
)

VERDICT_PROMPT_TEMPLATE = (
    "The debate is over. Below is the exchange, round by round. Each round "
    "shows your neutral restatement of the Answerer's position, the Critic's "
    "objection to it, and the Answerer's response to that objection (if any):"
    "\n\n"
    "{debate_transcript}\n\n"
    "Now issue your verdict. Weigh each objection against the Answerer's "
    "response to it: an objection the Answerer rebutted with substance should "
    "count differently from one it conceded or left unanswered. State the "
    "final answer in plain language, your confidence (low / medium / high) "
    "with a one-sentence justification, and note any unresolved uncertainty."
)

# Shown in place of the transcript if the debate produced no rounds at all.
_EMPTY_TRANSCRIPT = "(the Critic raised no objections)"

# Whole-word match for a confidence label, used to score the verdict.
_CONFIDENCE_LABEL = re.compile(r"\b(low|medium|high)\b", re.IGNORECASE)


def parse_confidence(verdict):
    """Extract the verdict's low/medium/high confidence label, or None.

    The verdict prompt asks for a confidence "(low / medium / high)", so we
    anchor on the word "confidence" and take the nearest label that follows it
    (the common "Confidence: medium" / "medium confidence" shapes). If there's
    no "confidence" anchor we fall back to the first standalone label in the
    text. Returns the lowercased label or None when nothing matches.
    """
    lowered = verdict.lower()
    anchor = lowered.find("confidence")
    if anchor != -1:
        after = _CONFIDENCE_LABEL.search(lowered, anchor)
        if after is not None:
            return after.group(1)
        # "medium confidence" — look just before the anchor too.
        before = lowered[max(0, anchor - 20):anchor]
        prior = list(_CONFIDENCE_LABEL.finditer(before))
        if prior:
            return prior[-1].group(1)
    match = _CONFIDENCE_LABEL.search(lowered)
    return match.group(1) if match else None


PHASE3_PROMPT = (
    "The debate has ended. Following the PHASE 3 rules in your "
    "instructions, produce exactly one line:\n"
    "  PROFILE_NOTE: he might tend to <one short sentence>\n"
    "  PROFILE_NOTE: none\n"
    "Remember: only report tendencies that the Critic explicitly "
    "criticized; ignore all stylistic matters; phrase tentatively."
)


class DebatePipeline:
    """One full round per call to `run(question)`."""

    # One round = (Answerer reply, Judge restatement, Critic critique). The
    # loop ends early when the Critic says "no further objections".
    MAX_DEBATE_ROUNDS = 3

    # How many times we nudge the Judge to use protocol tokens before
    # aborting the question.
    MAX_REPHRASE_NUDGES = 3

    # Case-insensitive substring the Critic uses to signal it's done.
    CRITIC_DONE_MARKER = "no further objections"

    def __init__(self, ctx):
        self.ctx = ctx

    async def run(self, user_question):
        self._reset_per_round()

        stats = self.ctx.stats
        stats.questions += 1
        t0 = time.monotonic()
        t1 = None
        try:
            rephrased = await self._rephrase(user_question)
            if rephrased is None:
                return

            t1 = time.monotonic()
            await self._debate(rephrased)

            self.ctx.prior_rephrased.append(rephrased)
            await self._extract_profile_note()
        finally:
            t_end = time.monotonic()
            dt_total = t_end - t0
            stats.wall_time_total += dt_total
            stats.last_wall_time_total = dt_total
            if t1 is not None:
                dt_post = t_end - t1
                stats.wall_time_post_rephrase += dt_post
                stats.last_wall_time_post_rephrase = dt_post

    # Phase 0

    def _reset_per_round(self):
        """Drop per-round actors so they're rebuilt with fresh system prompts."""
        self.ctx.critic.invalidate()
        self.ctx.judge.invalidate()

    # Phase 1

    async def _rephrase(self, user_question):
        judge = self.ctx.judge
        stats = self.ctx.stats
        reply = await judge.send(user_question)
        stats.add_tokens("rephrase", user_question, reply)
        print_judge_message(reply)

        nudges_used = 0
        while True:
            if "REPHRASED:" in reply:
                rephrased = reply.split("REPHRASED:", 1)[1].strip()
                return rephrased

            if "CLARIFY:" in reply:
                stats.clarifications += 1
                try:
                    user_reply = prompt_line("    your reply >>> ")
                except (EOFError, KeyboardInterrupt):
                    print()
                    return None
                if not user_reply.strip():
                    print_warning("clarification skipped — aborting this question")
                    return None
                reply = await judge.send(user_reply)
                stats.add_tokens("rephrase", user_reply, reply)
                print_judge_message(reply)
                continue

            if nudges_used >= self.MAX_REPHRASE_NUDGES:
                print_warning(
                    f"judge failed to emit CLARIFY or REPHRASED after "
                    f"{self.MAX_REPHRASE_NUDGES} nudges — aborting this question"
                )
                return None

            nudges_used += 1
            print_warning(
                f"judge did not emit CLARIFY or REPHRASED; nudging "
                f"({nudges_used}/{self.MAX_REPHRASE_NUDGES})"
            )
            reply = await judge.send(NUDGE_PROMPT)
            stats.add_tokens("rephrase", NUDGE_PROMPT, reply)
            print_judge_message(reply)

    # Phase 2

    async def _debate(self, rephrased_question):
        answerer = self.ctx.answerer
        critic = self.ctx.critic
        judge = self.ctx.judge
        stats = self.ctx.stats

        # First Answerer turn — fed the rephrased question directly.
        answerer_reply = await answerer.send(rephrased_question)
        stats.add_tokens("answerer", rephrased_question, answerer_reply)
        print_answerer(answerer_reply)

        # One record per round so the verdict can be handed the exchange as
        # paired (restatement -> objection -> response) units rather than a
        # flat list of objections detached from what they were answering.
        rounds = []
        for _ in range(self.MAX_DEBATE_ROUNDS):
            stats.debate_rounds += 1
            # Profile snippet is rebuilt into the Critic's system prompt every
            # round; count it here so its cost is visible separately from the
            # Critic and verdict buckets. Per the session-stats plan, some
            # overlap with the critic bucket is accepted.
            stats.add_tokens(
                "profile",
                render_answerer_profile(self.ctx.active_profile()),
            )

            # The channel constraint: Judge re-expresses the Answerer's reply
            # as neutral facts. The Critic only ever sees this restatement.
            restate_prompt = RESTATE_PROMPT_TEMPLATE.format(answer=answerer_reply)
            restatement = await judge.send(restate_prompt)
            stats.add_tokens("critic", restate_prompt, restatement)
            print_restatement(restatement)

            critic_reply = await critic.send(restatement)
            stats.add_tokens("critic", restatement, critic_reply)
            print_critic(critic_reply)

            record = {
                "restatement": restatement,
                "critique": critic_reply,
                "response": None,
            }
            rounds.append(record)

            if self.CRITIC_DONE_MARKER in critic_reply.lower():
                break

            # The Answerer hears the critique raw and responds. That response
            # is recorded against this round's objection so the verdict can see
            # which objections were actually rebutted.
            answerer_reply = await answerer.send(critic_reply)
            stats.add_tokens("answerer", critic_reply, answerer_reply)
            print_answerer(answerer_reply)
            record["response"] = answerer_reply

        # The Judge has every Answerer reply in its memory (as input to the
        # restate prompts) but has never seen the Critic. Hand it the exchange
        # as an interleaved transcript so it can pair each objection with the
        # Answerer's response to it.
        verdict_prompt = VERDICT_PROMPT_TEMPLATE.format(
            debate_transcript=self._format_debate_transcript(rounds)
        )
        verdict = await judge.send(verdict_prompt)
        stats.add_tokens("verdict", verdict_prompt, verdict)
        stats.record_confidence(parse_confidence(verdict))
        print_final_answer(verdict)

    @staticmethod
    def _format_debate_transcript(rounds):
        """Render the round records as an interleaved verdict transcript.

        Each round is shown as the Answerer's (restated) position, the Critic's
        objection, and the Answerer's response to it. A round whose objection
        ended the debate carries no response, which is itself a signal to the
        Judge (an unanswered objection).
        """
        if not rounds:
            return _EMPTY_TRANSCRIPT

        blocks = []
        for i, r in enumerate(rounds, 1):
            response = r["response"]
            if response is None:
                response = "(none — the debate ended on this objection)"
            blocks.append(
                f"Round {i}\n"
                f"  Answerer position (your restatement): {r['restatement']}\n"
                f"  Critic objection: {r['critique']}\n"
                f"  Answerer response: {response}"
            )
        return "\n\n".join(blocks)

    # Phase 3

    async def _extract_profile_note(self):
        if not self.ctx.config.build_profile:
            return

        try:
            reply = await self.ctx.judge.send(PHASE3_PROMPT)
        except Exception as e:
            print_warning(f"profile extraction failed: {e!r}")
            return

        self.ctx.stats.add_tokens("profile", PHASE3_PROMPT, reply)

        if "PROFILE_NOTE:" not in reply:
            return

        note = reply.split("PROFILE_NOTE:", 1)[1].strip().rstrip(".")

        if not note or note.lower() in ("none", "no", "n/a", "nothing"):
            return

        is_styl, matched = is_stylistic(note)
        if is_styl:
            print_warning(f"profile note rejected — stylistic ('{matched}'): {note}")
            return

        self.ctx.record_profile_note(note)

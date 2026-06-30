"""
Command pattern.

Each command is a class with:
  - name: the trigger string (e.g. "help", "new", "stats")
  - help: short description shown by !help
  - async execute(ctx, args): returns a CommandResult

The registry holds them and dispatches by name. `CommandResult.exit` tells
the chat loop to terminate.
"""


class CommandResult:
    def __init__(self, handled=True, exit=False):
        self.handled = handled
        self.exit = exit


class Command:
    name = ""
    help = ""

    async def execute(self, ctx, args):
        raise NotImplementedError


class CommandRegistry:
    def __init__(self):
        self._commands = {}

    def register(self, command):
        self._commands[command.name] = command

    def get(self, name):
        return self._commands.get(name)

    def all(self):
        return list(self._commands.values())

    async def dispatch(self, ctx, line):
        """
        line is the full input. We expect it to start with '!'.
        Returns None if not a command.
        """
        if not line.startswith("!"):
            return None

        # Strip leading '!' and split into name + args.
        body = line[1:].strip()
        if " " in body:
            name, args = body.split(maxsplit=1)
        else:
            name, args = body, ""

        cmd = self.get(name)
        if cmd is None:
            from ..ui import print_warning
            print_warning(f"unknown command: !{name} (type !help)")
            return CommandResult(handled=True, exit=False)

        return await cmd.execute(ctx, args)

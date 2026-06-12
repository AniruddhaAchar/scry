using System.CommandLine;

namespace Scry.Client.Cli;

/// <summary>Dump-analysis verbs: stack, dumpheap, dumpexceptions, printexception, dumpobject, dumparray.</summary>
internal static class AnalysisCommands
{
    public static IEnumerable<Command> Build(Option<bool> verbose)
    {
        yield return Stack(verbose);
        yield return DumpHeap(verbose);
        yield return DumpExceptions(verbose);
        yield return PrintException(verbose);
        yield return DumpObject(verbose);
        yield return DumpArray(verbose);
    }

    private static Command Stack(Option<bool> verbose)
    {
        var handleArg = CliOptions.HandleArg();
        var handleOption = CliOptions.HandleOption();
        var thread = new Option<uint?>("--thread") { Description = "Only walk this OS thread id (default: all managed threads)." };
        var timeout = CliOptions.TimeoutOption(10);
        var cmd = new Command("stack", "Print managed thread stack traces as JSON.");
        cmd.Arguments.Add(handleArg);
        cmd.Options.Add(handleOption);
        cmd.Options.Add(thread);
        cmd.Options.Add(timeout);
        cmd.SetAction((pr, ct) => CliRunner.Run(pr, verbose, (commands, c) =>
            commands.StackAsync(
                CliRunner.Handle(pr, handleArg, handleOption),
                pr.GetValue(thread),
                pr.GetValue(timeout),
                c), ct));
        return cmd;
    }

    private static Command DumpHeap(Option<bool> verbose)
    {
        var handleArg = CliOptions.HandleArg();
        var handleOption = CliOptions.HandleOption();
        var type = new Option<string?>("--type") { Description = "Case-sensitive substring filter on the full type name." };
        var stat = new Option<bool>("--stat") { Description = "Force per-type statistics (default when no --type)." };
        var limit = CliOptions.LimitOption(1000, "Max objects per listing page.");
        var offset = CliOptions.OffsetOption("Object-listing page offset.");
        var timeout = CliOptions.TimeoutOption(30, "RPC timeout in seconds (0 = none). The first heap command warms a snapshot.");
        var cmd = new Command("dumpheap", "Heap statistics, or a paged object listing with --type.");
        cmd.Arguments.Add(handleArg);
        cmd.Options.Add(handleOption);
        cmd.Options.Add(type);
        cmd.Options.Add(stat);
        cmd.Options.Add(limit);
        cmd.Options.Add(offset);
        cmd.Options.Add(timeout);
        cmd.SetAction((pr, ct) => CliRunner.Run(pr, verbose, (commands, c) =>
            commands.DumpHeapAsync(
                CliRunner.Handle(pr, handleArg, handleOption),
                pr.GetValue(type),
                pr.GetValue(stat),
                pr.GetValue(limit),
                pr.GetValue(offset),
                pr.GetValue(timeout),
                c), ct));
        return cmd;
    }

    private static Command DumpExceptions(Option<bool> verbose)
    {
        var handleArg = CliOptions.HandleArg();
        var handleOption = CliOptions.HandleOption();
        var limit = CliOptions.LimitOption(1000, "Max exceptions per page.");
        var offset = CliOptions.OffsetOption("Page offset.");
        var timeout = CliOptions.TimeoutOption(30);
        var cmd = new Command("dumpexceptions", "List live exceptions on the heap (address, type, message, HResult, inner chain).");
        cmd.Arguments.Add(handleArg);
        cmd.Options.Add(handleOption);
        cmd.Options.Add(limit);
        cmd.Options.Add(offset);
        cmd.Options.Add(timeout);
        cmd.SetAction((pr, ct) => CliRunner.Run(pr, verbose, (commands, c) =>
            commands.DumpExceptionsAsync(
                CliRunner.Handle(pr, handleArg, handleOption),
                pr.GetValue(limit),
                pr.GetValue(offset),
                pr.GetValue(timeout),
                c), ct));
        return cmd;
    }

    private static Command PrintException(Option<bool> verbose)
    {
        var handleOption = CliOptions.HandleOption();
        var address = CliOptions.AddressOption("Exception object address (hex, e.g. 0xCAFEBABE).");
        var timeout = CliOptions.TimeoutOption(10);
        var cmd = new Command("printexception", "Full detail for one exception by address, including its stack trace.");
        cmd.Options.Add(handleOption);
        cmd.Options.Add(address);
        cmd.Options.Add(timeout);
        cmd.SetAction((pr, ct) => CliRunner.Run(pr, verbose, (commands, c) =>
            commands.PrintExceptionAsync(
                pr.GetValue(handleOption),
                pr.GetValue(address),
                pr.GetValue(timeout),
                c), ct));
        return cmd;
    }

    private static Command DumpObject(Option<bool> verbose)
    {
        var handleOption = CliOptions.HandleOption();
        var address = CliOptions.AddressOption("Object address (hex, e.g. 0xFACADE).");
        var timeout = CliOptions.TimeoutOption(10);
        var cmd = new Command("dumpobject", "Dump an object's fields by address.");
        cmd.Options.Add(handleOption);
        cmd.Options.Add(address);
        cmd.Options.Add(timeout);
        cmd.SetAction((pr, ct) => CliRunner.Run(pr, verbose, (commands, c) =>
            commands.DumpObjectAsync(
                pr.GetValue(handleOption),
                pr.GetValue(address),
                pr.GetValue(timeout),
                c), ct));
        return cmd;
    }

    private static Command DumpArray(Option<bool> verbose)
    {
        var handleOption = CliOptions.HandleOption();
        var address = CliOptions.AddressOption("Array address (hex, e.g. 0xC0FFEE).");
        var limit = CliOptions.LimitOption(1000, "Max elements per page.");
        var offset = CliOptions.OffsetOption("Element page offset.");
        var timeout = CliOptions.TimeoutOption(30);
        var cmd = new Command("dumparray", "Dump an array's elements by address (paged).");
        cmd.Options.Add(handleOption);
        cmd.Options.Add(address);
        cmd.Options.Add(limit);
        cmd.Options.Add(offset);
        cmd.Options.Add(timeout);
        cmd.SetAction((pr, ct) => CliRunner.Run(pr, verbose, (commands, c) =>
            commands.DumpArrayAsync(
                pr.GetValue(handleOption),
                pr.GetValue(address),
                pr.GetValue(limit),
                pr.GetValue(offset),
                pr.GetValue(timeout),
                c), ct));
        return cmd;
    }
}

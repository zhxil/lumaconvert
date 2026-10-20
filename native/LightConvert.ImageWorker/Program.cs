using LightConvert.Core;

// Short-lived decoder: native image allocations are returned to Windows on exit.
try
{
    if (args.Length is < 4 or > 5) throw new ArgumentException("Invalid worker arguments");
    var converter = new MediaConverter(Path.Combine(AppContext.BaseDirectory, "tools"));
    if (args[0] == "thumbnail")
        await File.WriteAllBytesAsync(args[2], await converter.CreateThumbnailAsync(args[1], default, int.Parse(args[3])));
    else if (args[0] == "convert")
    {
        var captured = MediaConverter.ConvertPhoto(args[1], args[2], CompressionPreset.All[int.Parse(args[3])], null, default, args.Length < 5 || bool.Parse(args[4]));
        if (captured is not null) Console.WriteLine(captured.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }
    else throw new ArgumentException("Invalid worker command");
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    Environment.ExitCode = 1;
}

using DrillPress.Cli;

Console.OutputEncoding = new System.Text.UTF8Encoding(false);
return (int)await new CliApplication().RunAsync(args);

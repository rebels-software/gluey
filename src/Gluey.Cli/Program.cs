// Copyright (C) 2026 Rebels Software
//
// Licensed under the Apache License, Version 2.0 (the "License")
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.CommandLine;
using System.CommandLine.Invocation;
using Gluey.Parser;

var rootCommand = new RootCommand("Gluey - IoT Message Router CLI");

// validate command
var validateCommand = new Command("validate", "Validate a .gflow file syntax");
var fileArgument = new Argument<FileInfo>("file", "The .gflow file to validate");
validateCommand.AddArgument(fileArgument);

validateCommand.SetHandler(async (InvocationContext context) =>
{
    var file = context.ParseResult.GetValueForArgument(fileArgument);
    var exitCode = await ValidateFile(file);
    context.ExitCode = exitCode;
});

rootCommand.AddCommand(validateCommand);

return await rootCommand.InvokeAsync(args);

static async Task<int> ValidateFile(FileInfo file)
{
    if (!file.Exists)
    {
        Console.Error.WriteLine($"Error: File not found: {file.FullName}");
        return 1;
    }

    try
    {
        var source = await File.ReadAllTextAsync(file.FullName);

        // Tokenize
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();

        // Parse
        var parser = new Gluey.Parser.Parser(tokens);
        var flow = parser.Parse();

        Console.WriteLine($"✓ Valid: {file.Name}");
        return 0;
    }
    catch (ParseException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

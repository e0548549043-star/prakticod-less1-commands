using System.CommandLine;

// ===================== פונקציות =====================

// סינון קבצים לפי השפות שהמשתמש בחר
static string[] LanguageFilter(string[] files, string[] languages)
{
    var languageExtensions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["csharp"] = [".cs"],
        ["java"] = [".java"],
        ["python"] = [".py"],
        ["javascript"] = [".js"],
        ["typescript"] = [".ts"],
        ["html"] = [".html", ".htm"],
        ["css"] = [".css"],
        ["cpp"] = [".cpp", ".h"],
        ["sql"] = [".sql"]
    };

    HashSet<string> extensions;

    if (languages.Contains("all", StringComparer.OrdinalIgnoreCase))
    {
        extensions = languageExtensions.Values
                                       .SelectMany(ext => ext)
                                       .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
    else
    {
        extensions = languages.Where(lang => languageExtensions.ContainsKey(lang))
                              .SelectMany(lang => languageExtensions[lang])
                              .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    return files.Where(file => extensions.Contains(Path.GetExtension(file))).ToArray();
}

// מיון הקבצים לפי שם או לפי סוג
static string[] SortFiles(string[] files, string sort)
{
    if (sort == "type")
    {
        return files.OrderBy(file => Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
                    .ToArray();
    }

    return files.OrderBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
                .ToArray();
}

// כתיבת קובץ ה-bundle
static bool WriteBundle(string[] files, FileInfo output, string? author,
                        bool note, bool removeEmpty, string baseDir)
{
    try
    {
        using var writer = new StreamWriter(output.FullName);

        if (!string.IsNullOrWhiteSpace(author))
        {
            writer.WriteLine($"// Author: {author}");
            writer.WriteLine();
        }

        foreach (var file in files)
        {
            if (note)
            {
                writer.WriteLine($"// Source: {Path.GetRelativePath(baseDir, file)}");
            }

            var lines = File.ReadAllLines(file);

            if (removeEmpty)
            {
                lines = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            }

            foreach (var line in lines)
            {
                writer.WriteLine(line);
            }

            writer.WriteLine();
        }

        return true;
    }
    catch (UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"error: no permission to write to {output.FullName}");
        return false;
    }
    catch (IOException ex)
    {
        Console.Error.WriteLine($"error: couldn't save the file: {ex.Message}");
        return false;
    }
}

// הצגת שאלה וקליטת תשובה
static string Ask(string question)
{
    Console.Write(question);
    return Console.ReadLine()?.Trim() ?? "";
}

// שאלת כן/לא
static bool AskYesNo(string question)
{
    var answer = Ask(question).ToLower();
    return answer == "y" || answer == "yes";
}

// ===================== הגדרת הפקודות =====================

var rootCommand = new RootCommand("root command for fib bundle cli");

// ---------- פקודת bundle ----------
var bundleCommand = new Command("bundle", "bundle code file");

var languageOption = new Option<string[]>("--language", "-l")
{
    Description = "language to bundle",
    Required = true,
    AllowMultipleArgumentsPerToken = true,
};
bundleCommand.Options.Add(languageOption);

var outputOption = new Option<FileInfo>("--outputOption", "-ou")
{
    Description = "fileName and path",
    DefaultValueFactory = _ => new FileInfo("bundle.txt")
};
bundleCommand.Options.Add(outputOption);

var noteOption = new Option<bool>("--note", "-f") { Description = "if showing the sources" };
bundleCommand.Options.Add(noteOption);

var sortOption = new Option<string>("--sort", "-s")
{
    Description = "how to sort the files",
    DefaultValueFactory = _ => "name"
};
sortOption.AcceptOnlyFromAmong("name", "type");
bundleCommand.Options.Add(sortOption);

var removeEmptyLineOption = new Option<bool>("--remove-empty-line", "-r") { Description = "remove empty lines" };
bundleCommand.Options.Add(removeEmptyLineOption);

var authorOption = new Option<string>("--author", "-a")
{
    Description = "write the author name in the file"
};
bundleCommand.Options.Add(authorOption);

rootCommand.Add(bundleCommand);

bundleCommand.SetAction(parseResult =>
{
    var languages = parseResult.GetValue(languageOption)!;
    var output = parseResult.GetValue(outputOption)!;
    var note = parseResult.GetValue(noteOption);
    var sort = parseResult.GetValue(sortOption)!;
    var removeEmpty = parseResult.GetValue(removeEmptyLineOption);
    var author = parseResult.GetValue(authorOption);

    // יצירת תיקיית היעד אם צריך
    try
    {
        if (output.Directory != null && !output.Directory.Exists)
            output.Directory.Create();
    }
    catch (Exception)
    {
        Console.Error.WriteLine("error: couldn't create directory");
        return;
    }

    // איסוף כל הקבצים
    var baseDir = Directory.GetCurrentDirectory();
    string[] allfiles;
    try
    {
        allfiles = Directory.GetFiles(baseDir, "*", SearchOption.AllDirectories);
    }
    catch (Exception)
    {
        Console.Error.WriteLine("error: permissions denied");
        return;
    }

    // סינון תיקיות מוחרגות וקובץ הפלט עצמו
    var excludedDirs = new[] { "bin", "obj", ".vs", ".git", "node_modules", "packages", "publish" };
    var sep = Path.DirectorySeparatorChar;
    var candidates = allfiles.Where(f => !excludedDirs.Any(d =>
                                  f.Contains(sep + d + sep, StringComparison.OrdinalIgnoreCase)) &&
                                  !string.Equals(f, output.FullName, StringComparison.OrdinalIgnoreCase))
                             .ToArray();

    // סינון לפי שפה
    candidates = LanguageFilter(candidates, languages);

    if (candidates.Length == 0)
    {
        Console.Error.WriteLine("no matching files were found");
        return;
    }

    // מיון
    candidates = SortFiles(candidates, sort);

    // כתיבה לקובץ
    if (!WriteBundle(candidates, output, author, note, removeEmpty, baseDir))
        return;

    Console.WriteLine($"bundle created: {output.FullName}");
});

// ---------- פקודת create-rsp ----------
var createRspCommand = new Command("create-rsp", "create a response file with a ready bundle command");
rootCommand.Add(createRspCommand);

createRspCommand.SetAction(parseResult =>
{
    // language – חובה, שואלים עד שמתקבלת תשובה
    string languages;
    do
    {
        languages = Ask("Languages to bundle (separated by spaces, or 'all'): ");
    } while (languages == "");

    // output – אם ריק, תיכנס ברירת המחדל
    var output = Ask("Output file name or path (Enter for default): ");

    // note – כן/לא
    var note = AskYesNo("Write the source path as a comment? (y/n): ");

    // sort – רק name או type, או ריק לברירת מחדל
    string sort;
    do
    {
        sort = Ask("Sort by 'name' or 'type' (Enter for name): ").ToLower();
    } while (sort != "" && sort != "name" && sort != "type");

    // remove-empty-lines – כן/לא
    var removeEmpty = AskYesNo("Remove empty lines? (y/n): ");

    // author – אופציונלי
    var author = Ask("Author name (Enter to skip): ");

    // שם קובץ התגובה – קבוע
    var rspName = "bundle.rsp";

    // בניית הפקודה המלאה
    var command = $"bundle {languageOption.Name} {languages}";

    if (output != "")
        command += $" {outputOption.Name} \"{output}\"";
    if (note)
        command += $" {noteOption.Name}";
    if (sort != "")
        command += $" {sortOption.Name} {sort}";
    if (removeEmpty)
        command += $" {removeEmptyLineOption.Name}";
    if (author != "")
        command += $" {authorOption.Name} \"{author}\"";

    // שמירה לקובץ
    try
    {
        File.WriteAllText(rspName, command);
        Console.WriteLine($"response file created: {Path.GetFullPath(rspName)}");
        Console.WriteLine($"run it with: commands @{rspName}");
    }
    catch (Exception)
    {
        Console.Error.WriteLine($"error: couldn't save the response file {rspName}");
    }
});

// ===================== הרצה – חייב להיות בסוף =====================
rootCommand.Parse(args).Invoke();
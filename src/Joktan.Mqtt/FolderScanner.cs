using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt;

/// <summary>
/// Turns a folder into messages: every .json file below it is a message whose topic is the path
/// of the folder that holds it.
/// </summary>
public static class FolderScanner
{
    public static List<FolderMessage> Scan(string root)
    {
        return Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(MessageSettings.FileSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(path => Read(root, path))
            .OrderBy(m => m.Topic, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static FolderMessage Read(string root, string path)
    {
        var relative = Path.GetRelativePath(root, Path.GetDirectoryName(path)!);
        var folder = relative == "."
            ? ""
            : relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

        string text = "";
        JsonNode? body = null;
        var settings = MessageSettings.Default;
        string? error = null;

        try
        {
            text = File.ReadAllText(path);
            body = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false });
            if (body is null)
                error ??= Strings.FolderScanner_TheFileHoldsNoJSONValue;
        }
        catch (JsonException)
        {
            error ??= Strings.FolderScanner_NotValidJSON;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error ??= string.Format(Strings.FolderScanner_CannotBeRead, e.Message);
        }

        var settingsPath = MessageSettings.PathFor(path);
        if (body is not null && File.Exists(settingsPath))
        {
            try
            {
                var parsed = MessageSettings.Parse(File.ReadAllText(settingsPath));
                if (VaryingFields.FindMissing(body, parsed.Fields) is { } missing)
                    error ??= string.Format(Strings.FolderScanner_SettingsNameAFieldThatIs, missing);
                else if (parsed.Arrays.Select(a => VaryingFields.CheckArray(body, a)).FirstOrDefault(e => e is not null) is { } arrayError)
                    error ??= arrayError;
                else
                    settings = parsed;
                if (parsed.TopicFields.FirstOrDefault(f => !TopicTemplate.IsValidName(f.Path)) is { } badName)
                    error ??= string.Format(Strings.FolderScanner_CannotBeATopicFieldName, badName.Path);
            }
            catch (FormatException e)
            {
                error ??= string.Format(Strings.FolderScanner_SettingsFileIsNotValid, e.Message);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                error ??= string.Format(Strings.FolderScanner_SettingsFileCannotBeRead, e.Message);
            }
        }

        // A topic from the settings replaces the folder path, so the file may then sit anywhere.
        var topic = TopicTemplate.Effective(folder, settings.Topic);
        if (topic.Length == 0)
            error ??= Strings.FolderScanner_NotInsideATopicFolderAnd;
        else if (TopicTemplate.Check(topic, settings.TopicFields) is { } topicError)
            error ??= topicError;

        return new FolderMessage
        {
            FilePath = path,
            FileName = Path.GetFileName(path),
            Folder = folder,
            Topic = topic,
            Text = text,
            Body = body,
            Settings = settings,
            Error = error,
        };
    }
}

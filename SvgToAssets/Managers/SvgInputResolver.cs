using System.Buffers;
using Meziantou.Framework;
using Meziantou.Framework.Globbing;
using SvgToAssets.Models;

namespace SvgToAssets.Managers;

/// <summary>
/// Expands SVG paths and glob patterns into distinct files, each with a unique output folder name.
/// </summary>
internal static class SvgInputResolver
{
    private static readonly SearchValues<char> GlobCharacters = SearchValues.Create("*?[{");

    private static readonly SlugOptions FolderNameOptions = new() { CasingTransformation = CasingTransformation.ToLowerCase };

    /// <summary>
    /// Resolves <paramref name="pathsOrPatterns"/> relative to <paramref name="workingDirectory"/>.
    /// </summary>
    /// <param name="pathsOrPatterns">File paths, or glob patterns such as <c>icons/**/*.svg</c>.</param>
    /// <param name="workingDirectory">Base for relative paths and patterns.</param>
    /// <param name="onError">Receives a message for each missing file, unmatched pattern, or output folder collision.</param>
    /// <returns>The matched files in input order, without duplicates.</returns>
    public static SvgInput[] Resolve(IEnumerable<string> pathsOrPatterns, FullPath workingDirectory, Action<string> onError)
    {
        ArgumentNullException.ThrowIfNull(pathsOrPatterns);
        ArgumentNullException.ThrowIfNull(onError);

        var seen = new HashSet<FullPath>();
        var inputs = new List<SvgInput>();
        foreach (var value in pathsOrPatterns)
        {
            foreach (var (path, name) in Expand(value, workingDirectory, onError))
            {
                if (seen.Add(path))
                {
                    inputs.Add(new SvgInput(path, Slug.Create(name, FolderNameOptions) ?? string.Empty));
                }
            }
        }

        // Folder names only matter when several SVGs share one output directory.
        if (inputs.Count > 1)
        {
            foreach (var input in inputs.Where(i => i.FolderName.Length == 0))
            {
                onError($"Cannot derive an output folder name from '{input.Path}'.");
            }

            var collisions = inputs
                .Where(i => i.FolderName.Length > 0)
                .GroupBy(i => i.FolderName, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Skip(1).Any());

            foreach (var collision in collisions)
            {
                onError($"{string.Join(" and ", collision.Select(i => $"'{i.Path}'"))} would share the '{collision.Key}' output folder.");
            }
        }

        return [.. inputs];
    }

    private static IEnumerable<(FullPath Path, string Name)> Expand(string value, FullPath workingDirectory, Action<string> onError)
    {
        var segments = value.Split(['/', '\\']);
        var globStart = Array.FindIndex(segments, segment => segment.AsSpan().ContainsAny(GlobCharacters));

        if (globStart < 0)
        {
            var path = FullPath.Combine(workingDirectory, value);
            if (!File.Exists(path))
            {
                onError($"File does not exist: '{path}'.");
                return [];
            }

            return [(path, path.NameWithoutExtension)];
        }

        // Everything before the first wildcard segment is a plain directory; the trailing '/' keeps "C:" meaning the drive root.
        var baseDirectory = globStart == 0
            ? workingDirectory
            : FullPath.Combine(workingDirectory, string.Join('/', segments[..globStart]) + "/");
        var pattern = string.Join('/', segments[globStart..]);

        if (!Glob.TryParse(pattern, GlobDialect.Standard, GlobOptions.IgnoreCase, out var glob))
        {
            onError($"Invalid glob pattern: '{value}'.");
            return [];
        }

        var matches = Directory.Exists(baseDirectory)
            ? glob.EnumerateFiles(baseDirectory)
                .Select(file => FullPath.Combine(baseDirectory, file))
                .Select(path => (path, Path.ChangeExtension(path.MakePathRelativeTo(baseDirectory), extension: null)))
                .ToList()
            : [];

        if (matches.Count == 0)
        {
            onError($"No files match '{value}'.");
        }

        return matches;
    }
}

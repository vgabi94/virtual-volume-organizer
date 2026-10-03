using System.CommandLine;
using VVO.Cli.Contract;
using VVO.Cli.Output;
using VVO.Core.Models;
using VVO.Core.Services;

namespace VVO.Cli.Commands;

public static class SearchCommand
{
    private const int DefaultLimit = 1000;

    public static Command Create(IServiceProvider services)
    {
        var db = DatabaseFile.CreateOption();
        var term = new Argument<string>("term") { Description = "Text the name has to contain." };
        var folder = new Option<Guid?>("--folder") { Description = "Search only the tree of this folder entry." };
        var limit = new Option<int>("--limit")
        {
            Description = $"The most hits to return. {DefaultLimit} when left out.",
            DefaultValueFactory = _ => DefaultLimit
        };

        var command = new Command(
            "search",
            "Find files and folders by name, the way the GUI's search does. A record listed under several "
            + "folders is a hit once for each.")
        {
            term, db, folder, limit
        };

        command.SetJsonAction(services, async context =>
        {
            var text = context.ParseResult.GetRequiredValue(term);
            if (string.IsNullOrWhiteSpace(text))
                throw CliException.Usage("The search term cannot be empty.");

            var most = context.ParseResult.GetValue(limit);
            if (most < 1)
                throw CliException.Usage("--limit has to be at least 1.");

            await DatabaseFile.OpenExistingAsync(context, db);

            var volumes = context.Service<IVirtualVolumeService>();
            var entries = await Catalogue.EntriesAsync(context);

            IReadOnlyCollection<FileRecord> found;
            if (context.ParseResult.GetValue(folder) is { } entryId)
            {
                var entry = entries.SingleOrDefault(item => item.Id == entryId)
                    ?? throw CliException.NotFound($"There is no folder entry '{entryId}'.");

                // The explorer's search within one folder
                found = await volumes.FindByNameAsync(text, entry.TreeId);

                entries = [entry];
            }
            else
            {
                // The explorer's search everywhere, which skips trees no folder is listed with
                var live = entries.Select(entry => entry.TreeId).ToHashSet();
                found = (await volumes.FindByNameAsync(text))
                    .Where(record => live.Contains(record.RootFolderId))
                    .ToList();
            }

            var known = (await Catalogue.WithAncestorsAsync(context, found)).ToDictionary(record => record.Id);

            // A record whose folders above are gone can't be placed; one such does not sink the search
            var placed = found.Where(record => Catalogue.IsPlaced(record, known)).ToList();
            var unplaced = found.Count - placed.Count;
            found = placed;

            var hits = new List<RecordDto>();
            foreach (var tree in found.GroupBy(record => record.RootFolderId))
            {
                var placer = await Catalogue.PlacerAsync(
                    context, entries.Where(entry => entry.TreeId == tree.Key).ToList(), known.Values);

                hits.AddRange(tree.SelectMany(placer.DescribeEach));
            }

            var ordered = hits
                .OrderByDescending(hit => hit.IsFolder)
                .ThenBy(hit => hit.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(hit => hit.Entries[0].CataloguePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new
            {
                Hits = ordered.Take(most).ToList(),
                Total = ordered.Count,
                Truncated = ordered.Count > most,
                Unplaced = unplaced
            };
        }, TableFormat.Search);

        return command;
    }
}

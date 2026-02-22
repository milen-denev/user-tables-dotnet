using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UserTables.Client.Transport;

namespace UserTables.Client.Internal;

internal sealed class SchemaAutoMigrator(IUserTablesTransport transport)
{
    public async Task<SchemaMigrationReport> MigrateAsync(
        IReadOnlyCollection<EntityMetadata> entities,
        bool addOnly,
        CancellationToken cancellationToken)
    {
        var added = 0;
        var removed = 0;
        var recreated = 0;
        var skipped = 0;

        foreach (var metadata in entities)
        {
            var existing = await transport.ListColumnsAsync(metadata.TableId, cancellationToken).ConfigureAwait(false);
            var existingByName = existing.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);
            var desiredByName = metadata.ExpectedColumns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var expected in metadata.ExpectedColumns)
            {
                if (!existingByName.TryGetValue(expected.Name, out var current))
                {
                    await transport.CreateColumnAsync(
                            metadata.TableId,
                            new CreateColumnRequest(expected.Name, expected.ValueTypeKey, expected.Required),
                            cancellationToken)
                        .ConfigureAwait(false);
                    added++;
                    continue;
                }

                if (!string.Equals(current.ValueTypeKey, expected.ValueTypeKey, StringComparison.OrdinalIgnoreCase))
                {
                    if (addOnly)
                    {
                        skipped++;
                        continue;
                    }

                    await transport.DeleteColumnAsync(current.Id, cancellationToken).ConfigureAwait(false);
                    await transport.CreateColumnAsync(
                            metadata.TableId,
                            new CreateColumnRequest(expected.Name, expected.ValueTypeKey, expected.Required),
                            cancellationToken)
                        .ConfigureAwait(false);
                    recreated++;
                }
            }

            foreach (var current in existing)
            {
                if (current.IsAutoRelation)
                {
                    continue;
                }

                if (!desiredByName.ContainsKey(current.Name))
                {
                    if (addOnly)
                    {
                        skipped++;
                        continue;
                    }

                    await transport.DeleteColumnAsync(current.Id, cancellationToken).ConfigureAwait(false);
                    removed++;
                }
            }
        }

        return new SchemaMigrationReport(added, removed, recreated, skipped);
    }
}

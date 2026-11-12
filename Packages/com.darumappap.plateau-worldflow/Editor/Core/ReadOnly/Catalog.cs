using System;
using System.Collections.Generic;
using System.Linq;

namespace DarumaPPAP.PlateauWorldFlow.ReadOnly
{
    internal sealed class Catalog
    {
        internal string Source { get; }
        internal string Reference { get; }
        internal DateTimeOffset CheckedAt { get; }
        internal DateTimeOffset ValidUntil { get; }
        internal bool Complete { get; }
        private readonly Dictionary<string, Observation> _observations;

        internal Catalog(string source, string reference, DateTimeOffset checkedAt, DateTimeOffset validUntil, IEnumerable<Observation> observations, bool complete = true)
        {
            if (!WireValues.Source(source) || !WireValues.Id(reference) || validUntil <= checkedAt)
            {
                throw new ArgumentException("Invalid catalog provenance.");
            }

            Source = source;
            Reference = reference;
            CheckedAt = checkedAt;
            ValidUntil = validUntil;
            Complete = complete;
            _observations = observations.ToDictionary(item => item.Dataset.Id, StringComparer.Ordinal);
        }

        internal IEnumerable<Observation> Observations => _observations.Values;
        internal bool TryGet(string id, out Observation observation) => _observations.TryGetValue(id, out observation);

        internal object Evidence() => Json.Object(("kind", "DATASET_QUERY"), ("source", Source == "LOCAL" ? "LOCAL_DATASET" : "REMOTE_DATASET"), ("checkedAt", Digests.Timestamp(CheckedAt)));
    }
}

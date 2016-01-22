using System;
using System.Collections.Generic;
using System.Linq;

namespace NuGet.Frameworks
{
    public class FallbackFramework : NuGetFramework, IEquatable<FallbackFramework>
    {
        /// <summary>
        /// Frameworks to fall back to, in order of precedence.
        /// </summary>
        public IList<NuGetFramework> Fallback { get; }

        public FallbackFramework(NuGetFramework framework, IList<NuGetFramework> fallbackFrameworks)
            : base(framework)
        {
            if (framework == null)
            {
                throw new ArgumentNullException(nameof(framework));
            }

            if (fallbackFrameworks == null)
            {
                throw new ArgumentNullException(nameof(fallbackFrameworks));
            }

            Fallback = fallbackFrameworks;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as FallbackFramework);
        }

        public override int GetHashCode()
        {
            var combiner = new HashCodeCombiner();

            combiner.AddInt32(NuGetFramework.Comparer.GetHashCode(this));
            foreach (var fallback in Fallback)
            {
                combiner.AddInt32(NuGetFramework.Comparer.GetHashCode(fallback));
            }

            return combiner.CombinedHash;
        }

        public bool Equals(FallbackFramework other)
        {
            if (other == null)
            {
                return false;
            }

            if (Object.ReferenceEquals(this, other))
            {
                return true;
            }

            return NuGetFramework.Comparer.Equals(this, other)
                   && Fallback.SequenceEqual(other.Fallback, NuGetFramework.Comparer);
        }
    }
}

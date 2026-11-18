using System.Runtime.CompilerServices;

// The NUnit project pins drops and dice through internal seams; Unity never sees that assembly.
[assembly: InternalsVisibleTo("DungeonTycoon.Core.Tests")]

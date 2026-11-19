using System.Runtime.CompilerServices;
using System.Windows;

// The headless WPF tests read a few of the views' own members (perf and testability batch, Q7). Nothing else sees them.
[assembly: InternalsVisibleTo("OpenTail.Bullshark.App.Tests")]

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]

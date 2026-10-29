#:project ../tools/DrillPress.BundleVerification/DrillPress.BundleVerification.csproj
#:property PublishAot=false

using System.IO.Abstractions;
using DrillPress.BundleVerification;

if (args is not [var inventory, var directory])
    throw new ArgumentException("Expected generator inventory and output directory.");
new SourceGeneratorCatalogWriter(new FileSystem()).Write(inventory, directory);

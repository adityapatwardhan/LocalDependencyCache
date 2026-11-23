using System.Text;
using LocalDependencyCache.Core;

namespace LocalDependencyCache.Tests;

public sealed class GalleryFeedParserTests
{
    [Fact]
    public void ParsePackages_extracts_tags_and_dependencies_from_atom_feed()
    {
        const string xml = """
<?xml version="1.0" encoding="utf-8"?>
<feed xmlns="http://www.w3.org/2005/Atom" xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata" xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices">
  <entry>
    <updated>2026-06-04T11:05:06Z</updated>
    <content type="application/xml">
      <m:properties>
        <d:Id>Sample.Module</d:Id>
        <d:Version>1.2.3</d:Version>
        <d:NormalizedVersion>1.2.3</d:NormalizedVersion>
        <d:Title>Sample Module</d:Title>
        <d:Description>Demo package.</d:Description>
        <d:Authors>Contoso</d:Authors>
        <d:Owners>contoso</d:Owners>
        <d:Published>2026-06-04T11:00:06Z</d:Published>
        <d:LastUpdated>2026-06-04T11:05:06Z</d:LastUpdated>
        <d:IsPrerelease>false</d:IsPrerelease>
        <d:Tags>PSCommand_Get-Sample Demo Utility</d:Tags>
        <d:Dependencies>Other.Module:[1.0.0,2.0.0)</d:Dependencies>
      </m:properties>
    </content>
  </entry>
</feed>
""";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var packages = GalleryFeedParser.ParsePackages(stream);

        var package = Assert.Single(packages);
        Assert.Equal("Sample.Module", package.Id);
        Assert.Equal("1.2.3", package.Version);
        Assert.Contains("PSCommand_Get-Sample", package.Tags);
        Assert.Contains("Demo", package.Tags);
        Assert.Contains("Utility", package.Tags);

        var dependency = Assert.Single(package.Dependencies);
        Assert.Equal("Other.Module", dependency.Id);
        Assert.Equal("1.0.0", dependency.MinVersion);
        Assert.Equal("2.0.0", dependency.MaxVersion);
        Assert.True(dependency.IncludeMin);
        Assert.False(dependency.IncludeMax);
    }
}
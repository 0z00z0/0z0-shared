# Consuming BrandAboutControl

1. Sibling `ProjectReference` to `..\0z0-shared\src\ZeroZero.Brand.WinUI\ZeroZero.Brand.WinUI.csproj`.
2. Host the control:

```xml
<Page ... xmlns:brand="using:ZeroZero.Brand.WinUI">
    <ScrollViewer>
        <brand:BrandAboutControl x:Name="About" MaxWidth="560" HorizontalAlignment="Center"/>
    </ScrollViewer>
</Page>
```

3. Populate it with your own app's data:

```csharp
About.SetInfo(new AboutInfo
{
    AppName           = "...",
    Version           = "...",
    Description       = "...",
    RepoUrl           = "...",
    ExternalLibraries = [ new ExternalLibrary("Name", "Author", "Purpose", "License") ],
});
```

4. Don't supply Mark/Company/Tagline/Website/Donate — the control provides those itself from
   studio-wide constants.
5. CI needs a sibling checkout of `0z00z0/0z0-shared` (no NuGet feed).

# Consuming BrandAboutControl

A checklist for a full windowed app adopting the shared About content control. The detail —
the `ProjectReference` recipe, the CI shapes, the sample XAML and code — is in
[the README's hosting section](README.md#option-b--hosted-in-the-apps-own-page-brandaboutcontrol).

1. Reference `ZeroZero.Brand.WinUI` from a sibling checkout of this repo, and give the consuming
   workflow a checkout of it too.
2. Host `BrandAboutControl` in the existing About page's XAML, in place of the bespoke layout.
3. Call `SetInfo(AboutInfo)` once from the page's constructor or `Loaded` handler.
4. Supply only app facts: name, version, description, repo URL, external-library credits. The
   control provides the studio mark, company name, tagline, website link and donate link itself.
5. Delete the bespoke About layout once the control renders; keep the app's own brand-facts class
   as the single source of truth for the data.

# Consuming BrandAboutControl

A checklist for a full windowed app adopting the shared About content control. The detail —
the two reference recipes, the CI shapes, the sample XAML and code — is in
[the README's hosting section](README.md#option-b--hosted-in-the-apps-own-page-brandaboutcontrol).

1. Reference `ZeroZero.Brand.WinUI`, either as a package off the studio feed — which authenticates
   every read, so the consuming workflow gains a `read:packages` token — or from a sibling checkout
   of this repo, in which case the workflow gains a checkout of it instead.
2. Host `BrandAboutControl` in the existing About page's XAML, in place of the bespoke layout.
3. Call `SetInfo(AboutInfo)` once from the page's constructor or `Loaded` handler.
4. Supply only app facts: name, version, description, repo URL, external-library credits. The
   control provides the studio mark, company name, tagline, website link and donate link itself.
5. Delete the bespoke About layout once the control renders; keep the app's own brand-facts class
   as the single source of truth for the data.

# Repository defaults sample

A small consuming repository that commits its Drill Press setup. See
[repository setup](../../docs/REPOSITORY_SETUP.md) for the file format and rules.

- `drillpress.json` names the rules project, the solution and an MSBuild property,
  leaves rule `SHOP003` out for now, and excludes `third_party/` and `eng/`.
- `src/Shop` is the application. It compiles the vendored `third_party/Vendor`
  source, which keeps its upstream style.
- `eng/Rules` is the rules project. It is part of `Shop.slnx` so that it builds with
  the rest, and its own source would otherwise break `SHOP001` and `SHOP003`.

The excluded code still loads: `SHOP002` inlines `VendorClock clock` in
`src/Shop/Checkout.cs` only because the vendored declarations are analysed.

From a copy of this directory with the tool pinned in `.config/dotnet-tools.json`
and `DrillPressVersion` in `Directory.Build.props` set to the same version:

```sh
dotnet tool restore
dotnet restore Shop.slnx
dotnet tool run drillpress -- check                    # SHOP001 and SHOP002 in Checkout.cs
dotnet tool run drillpress -- check --select SHOP003   # the ignored rule on its own
dotnet tool run drillpress -- fix                      # applies both fixes; exit 0
```

`check` works from any subdirectory, such as `src/Shop`, because the CLI finds
`drillpress.json` in a parent directory.

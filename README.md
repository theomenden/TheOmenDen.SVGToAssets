# SvgToAssets

A .NET tool that turns SVGs into the visual assets and `AppIcon.ico` a WinUI 3 app needs.

Runs on x64 Windows, Linux and macOS with the .NET 10 runtime.

## Install

Build the tool package and install it from the local folder:

```sh
dotnet pack -c Release -o artifacts
dotnet tool install --global SvgToAssets --add-source ./artifacts
```

## Usage

```sh
svgtoassets <svg>... [options]
```

| Option | Description | Default |
| --- | --- | --- |
| `-o`, `--output` | Directory to write the assets to; created if missing. | `./Assets` |
| `-t`, `--type` | `Assets` (the PNGs), `Icon` (`AppIcon.ico`), or `All`. | `All` |
| `-c`, `--category` | `Basic`: the WinUI template's 7 files. `Required`: every scale of the assets the default manifest references. `Optional`: the unreferenced tiles. `All`: `Required` + `Optional` and a 14-size `AppIcon.ico`. | `Required` |

Inputs can be files or glob patterns. When several SVGs are converted, each gets an output subfolder named after it
(e.g. `dark/App Logo.svg` becomes `dark-app-logo`).

```sh
svgtoassets logo.svg -o MyApp/Assets
svgtoassets "icons/**/*.svg" -o out -c All
```

External images and elements referenced by an SVG are only loaded from the local file system.

## Credits

The idea and the original tool come from [SvgToAssets](https://github.com/noeldev/SvgToAssets) by Noël Danjou.

Third-party components and their licenses are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## License

Licensed under the [GNU General Public License v3.0](LICENSE).

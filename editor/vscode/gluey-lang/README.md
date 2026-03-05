# Gluey Flow Language

Syntax highlighting for `.gflow` workflow definition files used by the [Gluey](https://github.com/rebels-software/gluey) IoT message router.

## Features

- Syntax highlighting for the `.gflow` DSL
- Bracket matching and auto-closing
- Comment toggling (`//` line comments, `/* */` block comments)
- Code folding on `{ }` blocks
- Word pattern support for dotted identifiers (e.g. `json.parse`, `decode.binary`)

### Highlighted elements

| Element | Examples |
|---------|----------|
| Keywords | `flow`, `from`, `route`, `on_error`, `schema`, `filter`, `transform` |
| Constants | `true`, `false`, `null`, `skip` |
| Plugin names | `json.parse`, `decode.binary`, `http`, `mqtt`, `sql`, `console` |
| Built-in functions | `now()`, `uuid()`, `split()`, `route_to()` |
| Operators | `->`, `\|`, `&&`, `\|\|`, `==`, `!=`, `>=`, `<=`, `+`, `-` |
| Strings | `"double quoted"`, `'single quoted'` |
| Numbers | `42`, `3.14`, `0xFF` |
| Comments | `// line comment`, `/* block comment */` |
| Version strings | `v1.0`, `v2.1` |
| Meta variables | `$meta.topic`, `$meta.source` |

## Installation

### From .vsix file (local)

1. Package the extension:

   ```bash
   cd editor/vscode/gluey-lang
   npx @vscode/vsce package
   ```

2. Install the generated `.vsix`:

   ```bash
   code --install-extension gluey-lang-0.1.0.vsix
   ```

### During development

Open the `editor/vscode/gluey-lang` folder in VS Code, then press `F5` to launch an Extension Development Host with the grammar loaded.

## Publishing to VS Code Marketplace

1. Create a publisher account at [Visual Studio Marketplace](https://marketplace.visualstudio.com/manage).

2. Get a Personal Access Token from [Azure DevOps](https://dev.azure.com). The token needs the **Marketplace > Manage** scope.

3. Log in with the CLI:

   ```bash
   npx @vscode/vsce login rebels-software
   ```

4. Publish:

   ```bash
   npx @vscode/vsce publish
   ```

   Or publish a specific version:

   ```bash
   npx @vscode/vsce publish 0.1.0
   ```

## How to package

```bash
cd editor/vscode/gluey-lang
npx @vscode/vsce package
```

This produces `gluey-lang-0.1.0.vsix` in the current directory.

## Screenshot

<!-- Add a screenshot of .gflow syntax highlighting here -->
![Syntax highlighting](screenshot-placeholder.png)

## License

Apache-2.0

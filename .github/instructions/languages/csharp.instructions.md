---
description: 'Guidelines for building C# applications'
applyTo: '**/*.cs'
---

# C# Development

## C# Instructions
- Always use the latest supported version of C# for the [DOTNET_VERSION]
- Always specify the specific language version using the `<LangVersion>` tag in the csproj file

| [DOTNET_VERSION] | C# Language Support |
| ---------------- | ------------------- |
| 10.0             | 14.0                |
| 9.0              | 13.0                |
| 8.0              | 12.0                |


## General Instructions
- Make only high confidence suggestions when reviewing code changes.
- Write code with good maintainability practices, including comments on why certain design decisions were made.
- Handle edge cases and write clear exception handling.
- For libraries or external dependencies, mention their usage and purpose in comments.
- When using non-ASCII characters, use Unicode escape sequences (\uXXXX) instead of literal characters.
- Only comment code when a "WHY" is required, and never comment "WHAT"

## Naming Conventions
- Internal and Private fields:
    - Use `_camelCase` for internal and private field names
    - Use `readonly` where possible
    - Prefix internal and private fields with `_`
    - Prefix static fields with `s_`
    - Prefix thread static fields with `t_`
    - When used on static fields, `readonly` should come after `static` (e.g. `static readonly`)
- Public fields:
    - Use sparingly
    - Use `PascalCasing` for names
    - Do not prefix public field names
- Use `PascalCasing` for all constant local variables and fields (unless calling interop code)
- Use `PascalCasing` for all method names, including local functions.
- Always use `nameof(...)` instead of hardcoded strings "..." to reference code
- Avoid `this.` unless absolutely necessary.
- Always specify the visibility, even if it's the default (e.g. `private string _foo`)
- Prefix interface names with "I" (e.g., `IUserService`).
- Postfix async methods with "Async" (e.g., `GetUserAsync`).
- use language keywords instead of BCL types
- All internal and private types should be static or sealed
- Primary constructor parameters should be named like parameters, using `camelCase` and not prefixed with `_`

## Formatting
- Fields should always go at the top of the class within type declarations
- Use Allman style braces 
    - each brace begins on a new line
    - single line statement blocks can go without braces, but the block must be indented on it's own line
    - Do not nest statement blocks without braces
    - `using` statements can be nested without braces at the same indentation level
    - never use single-line form
    - If any block of an `if` / `else if` /.../ `else` compound statement uses braces, then all blocks *must* use braces
- Use four spaces of indentation (no tabs)
- Namespace imports (`using ...`)
    - should be specified at the top of the file, _outside_ of `namespace` declarations
    - `System.*` namespaces should be placed at the very top, sorted alphabetically first
    - Other should be sorted alphabetically after any `System.*` namespace using statements
- Avoid more than one empty line at any time.
- Use `var` when the type is explicitly named or obvious on the right-hand side, due to `new` or value refs.
- Use target-typed `new()` only when the type is explicityly named on the left-hand side on the same line.

## Nullable Reference Types

- Declare variables non-nullable, and check for `null` at entry points.
- Use null pattern checking `is null` or `is not null` instead of `== null` or `!= null`.
- Trust the C# null annotations and don't add null checks when the type system says a value cannot be null.

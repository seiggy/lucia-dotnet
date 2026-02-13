# Dependency Changes

## Microsoft.Agents.AI 1.0.0-preview.251204.1 → 1.0.0-preview.260212.1

This document details all package reference and framework updates required for this migration.

---

## Framework Upgrade

### .NET 10 Required

The package now targets .NET 10.

**Update your project file:**

```xml
<!-- Before -->
<TargetFramework>net9.0</TargetFramework>

<!-- After -->
<TargetFramework>net10.0</TargetFramework>
```

**PR:** [#2128](https://github.com/microsoft/agent-framework/pull/2128)

---

## Core Package Updates

### Microsoft.Extensions.AI Packages

| Package | From | To | PRs |
|---------|------|-----|-----|
| `Microsoft.Extensions.AI` | 9.x / 10.0.x | 10.2.0 | [#2392](https://github.com/microsoft/agent-framework/pull/2392), [#2735](https://github.com/microsoft/agent-framework/pull/2735), [#3211](https://github.com/microsoft/agent-framework/pull/3211) |
| `Microsoft.Extensions.AI.Abstractions` | 9.x / 10.0.x | 10.2.0 | [#3211](https://github.com/microsoft/agent-framework/pull/3211) |
| `Microsoft.Extensions.AI.OpenAI` | 9.x / 10.0.x | 10.2.0 | [#3211](https://github.com/microsoft/agent-framework/pull/3211) |

**Update package references:**

```xml
<!-- Before -->
<PackageReference Include="Microsoft.Extensions.AI" Version="10.0.1" />
<PackageReference Include="Microsoft.Extensions.AI.Abstractions" Version="10.0.1" />
<PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="10.0.1" />

<!-- After -->
<PackageReference Include="Microsoft.Extensions.AI" Version="10.2.0" />
<PackageReference Include="Microsoft.Extensions.AI.Abstractions" Version="10.2.0" />
<PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="10.2.0" />
```

---

### OpenAI SDK

| Package | Version |
|---------|---------|
| `OpenAI` | 2.7.0 |

**PR:** [#2392](https://github.com/microsoft/agent-framework/pull/2392)

```xml
<PackageReference Include="OpenAI" Version="2.7.0" />
```

---

## Provider Package Updates

### Anthropic

| Package | Version | PR |
|---------|---------|-----|
| `Anthropic` | 12.0.1 | [#2993](https://github.com/microsoft/agent-framework/pull/2993) |

```xml
<PackageReference Include="Anthropic" Version="12.0.1" />
```

---

### Google GenAI

| Package | Version | PR |
|---------|---------|-----|
| `Google.GenAI` | 0.11.0 | [#3232](https://github.com/microsoft/agent-framework/pull/3232) |

```xml
<PackageReference Include="Google.GenAI" Version="0.11.0" />
```

---

### AWS Bedrock

| Package | Version | PR |
|---------|---------|-----|
| `AWSSDK.Extensions.Bedrock.MEAI` | 4.0.5.1 | [#2994](https://github.com/microsoft/agent-framework/pull/2994) |

```xml
<PackageReference Include="AWSSDK.Extensions.Bedrock.MEAI" Version="4.0.5.1" />
```

---

### Ollama (via Aspire)

| Package | Version | PR |
|---------|---------|-----|
| `CommunityToolkit.Aspire.OllamaSharp` | 13.0.0 | [#2856](https://github.com/microsoft/agent-framework/pull/2856) |

```xml
<PackageReference Include="CommunityToolkit.Aspire.OllamaSharp" Version="13.0.0" />
```

---

## Complete Package Reference Example

Here's a complete example of updated package references for a typical project:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- Core Microsoft.Agents.AI -->
    <PackageReference Include="Microsoft.Agents.AI" Version="1.0.0-preview.260212.1" />
    
    <!-- Microsoft.Extensions.AI ecosystem -->
    <PackageReference Include="Microsoft.Extensions.AI" Version="10.2.0" />
    <PackageReference Include="Microsoft.Extensions.AI.Abstractions" Version="10.2.0" />
    <PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="10.2.0" />
    
    <!-- OpenAI SDK -->
    <PackageReference Include="OpenAI" Version="2.7.0" />
    
    <!-- Optional: Provider-specific packages -->
    <!-- Anthropic -->
    <PackageReference Include="Anthropic" Version="12.0.1" />
    
    <!-- Google GenAI -->
    <PackageReference Include="Google.GenAI" Version="0.11.0" />
    
    <!-- AWS Bedrock -->
    <PackageReference Include="AWSSDK.Extensions.Bedrock.MEAI" Version="4.0.5.1" />
    
    <!-- Ollama via Aspire -->
    <PackageReference Include="CommunityToolkit.Aspire.OllamaSharp" Version="13.0.0" />
  </ItemGroup>

</Project>
```

---

## Version Compatibility Matrix

| Microsoft.Agents.AI | .NET | Microsoft.Extensions.AI | OpenAI SDK |
|---------------------|------|-------------------------|------------|
| 1.0.0-preview.251204.1 | 9.0+ | 10.0.1 | 2.7.0 |
| 1.0.0-preview.260212.1 | 10.0 | 10.2.0 | 2.7.0 |

---

## Migration Steps

1. **Update .NET SDK** to 10.0 or later
2. **Update target framework** in all project files to `net10.0`
3. **Update package references** using the versions listed above
4. **Run package restore:**
   ```bash
   dotnet restore
   ```
5. **Build and resolve any breaking changes** using the other migration documents

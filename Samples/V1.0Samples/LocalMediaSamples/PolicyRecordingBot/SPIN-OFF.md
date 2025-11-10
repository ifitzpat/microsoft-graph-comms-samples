# Teams Media Bot - Spin-Off Migration Guide

This guide provides step-by-step instructions for extracting the Teams Media Bot WebRTC implementation from the PolicyRecordingBot sample and creating a standalone repository.

## Table of Contents

- [Overview](#overview)
- [Architecture Summary](#architecture-summary)
- [Prerequisites](#prerequisites)
- [Directory Structure](#directory-structure)
- [Migration Steps](#migration-steps)
- [Files to Copy](#files-to-copy)
- [Creating New Project Files](#creating-new-project-files)
- [Path and Namespace Changes](#path-and-namespace-changes)
- [Dependencies](#dependencies)
- [Building the Project](#building-the-project)
- [Testing](#testing)
- [Configuration](#configuration)
- [Running the Application](#running-the-application)
- [Next Steps](#next-steps)

---

## Overview

This migration extracts the following components from the PolicyRecordingBot sample:

**C# Components:**
- **MediaBridge** - Bidirectional audio routing between Teams and WebRTC
- **AudioConverter** - PCM ↔ Opus codec conversion
- **WebRTCManager** - WebRTC peer connection management (SIPSorcery)
- **SignalingClient** - WebSocket client for signaling server communication
- **Supporting interfaces and data structures**

**Python Components:**
- **Signaling Server** - WebSocket-based signaling server for WebRTC negotiation
- **Unit tests** - pytest test suite

**Documentation:**
- Implementation guides for all major components
- API references and usage examples

---

## Architecture Summary

```
┌────────────────────────────────────────┐
│          Teams Call (Azure)            │
│   ↓ Microsoft Graph Communications    │
│        BotMediaStream (PCM)            │
└────────────────────────────────────────┘
                 ↓↑
┌────────────────────────────────────────┐
│         MediaBridge Component          │
│  ┌──────────────────────────────────┐ │
│  │  PCM → Opus (AudioConverter)     │ │
│  └──────────────────────────────────┘ │
│  ┌──────────────────────────────────┐ │
│  │  WebRTCManager (SIPSorcery)      │ │
│  └──────────────────────────────────┘ │
│  ┌──────────────────────────────────┐ │
│  │  SignalingClient (WebSocket)     │ │
│  └──────────────────────────────────┘ │
└────────────────────────────────────────┘
                 ↓↑
┌────────────────────────────────────────┐
│    Python Signaling Server (WS)        │
│  ┌──────────────────────────────────┐ │
│  │  SDP Offer/Answer Exchange       │ │
│  │  ICE Candidate Forwarding        │ │
│  └──────────────────────────────────┘ │
└────────────────────────────────────────┘
                 ↓↑
┌────────────────────────────────────────┐
│   GStreamer Service (or any WebRTC    │
│   client with Opus support)            │
└────────────────────────────────────────┘
```

---

## Prerequisites

**Development Environment:**
- Visual Studio 2019 or later (or Visual Studio Code with C# extension)
- .NET Framework 4.7.2 SDK or later
- MSBuild or .NET CLI
- Git

**Python Environment (for signaling server):**
- Python 3.8 or later
- pip (Python package manager)

**Optional:**
- Azure subscription (for Teams bot deployment)
- COTURN server (for TURN/STUN services in production)

---

## Directory Structure

Create the following directory structure for your new repository:

```
TeamsMediaBot/
├── README.md
├── LICENSE
├── .gitignore
├── SPIN-OFF.md (this file)
│
├── src/
│   ├── TeamsMediaBot.sln
│   │
│   ├── TeamsMediaBot.Core/
│   │   ├── TeamsMediaBot.Core.csproj
│   │   ├── Properties/
│   │   │   └── AssemblyInfo.cs
│   │   │
│   │   ├── WebRTC/
│   │   │   ├── MediaBridge.cs
│   │   │   ├── AudioConverter.cs
│   │   │   ├── AudioFrame.cs
│   │   │   ├── IAudioConverter.cs
│   │   │   ├── IWebRTCManager.cs
│   │   │   ├── WebRTCManager.cs
│   │   │   ├── README.md
│   │   │   └── WEBRTC_IMPLEMENTATION.md
│   │   │
│   │   └── Signaling/
│   │       ├── ISignalingClient.cs
│   │       ├── SignalingClient.cs
│   │       └── SIGNALING_CLIENT_IMPLEMENTATION.md
│   │
│   ├── TeamsMediaBot.Tests/
│   │   ├── TeamsMediaBot.Tests.csproj
│   │   ├── Properties/
│   │   │   └── AssemblyInfo.cs
│   │   │
│   │   ├── WebRTC/
│   │   │   ├── MediaBridgeTests.cs
│   │   │   └── WebRTCManagerTests.cs
│   │   │
│   │   ├── Signaling/
│   │   │   └── SignalingClientTests.cs
│   │   │
│   │   └── Helpers/
│   │       └── TestLogger.cs
│   │
│   └── TeamsMediaBot.Sample/
│       ├── TeamsMediaBot.Sample.csproj
│       ├── Properties/
│       │   └── AssemblyInfo.cs
│       ├── Program.cs
│       ├── Bot/
│       │   ├── Bot.cs
│       │   ├── CallHandler.cs
│       │   └── BotMediaStream.cs
│       ├── Http/
│       │   └── Controllers/
│       │       └── PlatformCallController.cs
│       └── appsettings.json
│
├── signaling/
│   ├── README.md
│   ├── requirements.txt
│   ├── requirements-dev.txt
│   ├── .gitignore
│   │
│   ├── src/
│   │   ├── __init__.py
│   │   └── signaling_server.py
│   │
│   └── tests/
│       ├── __init__.py
│       ├── conftest.py
│       └── test_signaling_server.py
│
└── docs/
    ├── ARCHITECTURE.md
    ├── DEPLOYMENT.md
    └── TROUBLESHOOTING.md
```

---

## Migration Steps

### Step 1: Initialize New Repository

```bash
# Create new repository
mkdir TeamsMediaBot
cd TeamsMediaBot
git init

# Create directory structure
mkdir -p src/TeamsMediaBot.Core/WebRTC
mkdir -p src/TeamsMediaBot.Core/Signaling
mkdir -p src/TeamsMediaBot.Core/Properties
mkdir -p src/TeamsMediaBot.Tests/WebRTC
mkdir -p src/TeamsMediaBot.Tests/Signaling
mkdir -p src/TeamsMediaBot.Tests/Helpers
mkdir -p src/TeamsMediaBot.Tests/Properties
mkdir -p src/TeamsMediaBot.Sample/Bot
mkdir -p src/TeamsMediaBot.Sample/Http/Controllers
mkdir -p src/TeamsMediaBot.Sample/Properties
mkdir -p signaling/src
mkdir -p signaling/tests
mkdir -p docs
```

### Step 2: Create .gitignore

Create `.gitignore` in the root directory:

```gitignore
# Build results
[Dd]ebug/
[Rr]elease/
x64/
x86/
[Bb]in/
[Oo]bj/
[Ll]og/
[Ll]ogs/

# Visual Studio
.vs/
*.suo
*.user
*.userosscache
*.sln.docstates
*.userprefs

# ReSharper
_ReSharper*/
*.DotSettings.user

# NuGet
packages/
*.nupkg
*.snupkg
project.lock.json
project.fragment.lock.json
artifacts/

# Test Results
TestResults/
*.trx
*.coverage
*.coveragexml

# Python
__pycache__/
*.py[cod]
*$py.class
*.so
.Python
env/
venv/
ENV/
.venv/
pip-log.txt
pip-delete-this-directory.txt
.pytest_cache/
.coverage
htmlcov/

# IDE
.vscode/
.idea/
*.swp
*.swo
*~

# OS
.DS_Store
Thumbs.db

# Application
appsettings.local.json
*.pfx
*.log
```

### Step 3: Copy Core Files

Copy the following files from the PolicyRecordingBot sample to your new repository:

#### WebRTC Components

```bash
# From PolicyRecordingBot source location
SOURCE_ROOT="<path-to>/PolicyRecordingBot/FrontEnd"

# Copy WebRTC files
cp "$SOURCE_ROOT/Bot/WebRTC/MediaBridge.cs" src/TeamsMediaBot.Core/WebRTC/
cp "$SOURCE_ROOT/Bot/WebRTC/AudioConverter.cs" src/TeamsMediaBot.Core/WebRTC/
cp "$SOURCE_ROOT/Bot/WebRTC/AudioFrame.cs" src/TeamsMediaBot.Core/WebRTC/
cp "$SOURCE_ROOT/Bot/WebRTC/IAudioConverter.cs" src/TeamsMediaBot.Core/WebRTC/
cp "$SOURCE_ROOT/Bot/WebRTC/IWebRTCManager.cs" src/TeamsMediaBot.Core/WebRTC/
cp "$SOURCE_ROOT/Bot/WebRTC/WebRTCManager.cs" src/TeamsMediaBot.Core/WebRTC/
cp "$SOURCE_ROOT/Bot/WebRTC/README.md" src/TeamsMediaBot.Core/WebRTC/
cp "$SOURCE_ROOT/Bot/WebRTC/WEBRTC_IMPLEMENTATION.md" src/TeamsMediaBot.Core/WebRTC/

# Copy Signaling files
cp "$SOURCE_ROOT/Signaling/ISignalingClient.cs" src/TeamsMediaBot.Core/Signaling/
cp "$SOURCE_ROOT/Signaling/SignalingClient.cs" src/TeamsMediaBot.Core/Signaling/
cp "$SOURCE_ROOT/Signaling/SIGNALING_CLIENT_IMPLEMENTATION.md" src/TeamsMediaBot.Core/Signaling/
```

#### Test Files

```bash
TEST_SOURCE="<path-to>/PolicyRecordingBot/PolicyRecordingBot.Tests"

# Copy test files
cp "$TEST_SOURCE/WebRTC/MediaBridgeTests.cs" src/TeamsMediaBot.Tests/WebRTC/
cp "$TEST_SOURCE/WebRTC/WebRTCManagerTests.cs" src/TeamsMediaBot.Tests/WebRTC/
cp "$TEST_SOURCE/Signaling/SignalingClientTests.cs" src/TeamsMediaBot.Tests/Signaling/
```

#### Python Signaling Server

```bash
SIGNALING_SOURCE="<path-to>/PolicyRecordingBot/signaling"

# Copy signaling server
cp -r "$SIGNALING_SOURCE/src/"* signaling/src/
cp -r "$SIGNALING_SOURCE/tests/"* signaling/tests/
cp "$SIGNALING_SOURCE/requirements.txt" signaling/
cp "$SIGNALING_SOURCE/requirements-dev.txt" signaling/
cp "$SIGNALING_SOURCE/README.md" signaling/
```

---

## Files to Copy

### Core Library Files (src/TeamsMediaBot.Core/)

| File | Source Path | Purpose |
|------|-------------|---------|
| `MediaBridge.cs` | `FrontEnd/Bot/WebRTC/MediaBridge.cs` | Audio routing between Teams and WebRTC |
| `AudioConverter.cs` | `FrontEnd/Bot/WebRTC/AudioConverter.cs` | PCM ↔ Opus conversion |
| `AudioFrame.cs` | `FrontEnd/Bot/WebRTC/AudioFrame.cs` | Audio frame data structure |
| `IAudioConverter.cs` | `FrontEnd/Bot/WebRTC/IAudioConverter.cs` | Audio converter interface |
| `IWebRTCManager.cs` | `FrontEnd/Bot/WebRTC/IWebRTCManager.cs` | WebRTC manager interface |
| `WebRTCManager.cs` | `FrontEnd/Bot/WebRTC/WebRTCManager.cs` | WebRTC peer connection management |
| `ISignalingClient.cs` | `FrontEnd/Signaling/ISignalingClient.cs` | Signaling client interface |
| `SignalingClient.cs` | `FrontEnd/Signaling/SignalingClient.cs` | WebSocket signaling client |

### Test Files (src/TeamsMediaBot.Tests/)

| File | Source Path | Purpose |
|------|-------------|---------|
| `MediaBridgeTests.cs` | `PolicyRecordingBot.Tests/WebRTC/MediaBridgeTests.cs` | MediaBridge unit tests |
| `WebRTCManagerTests.cs` | `PolicyRecordingBot.Tests/WebRTC/WebRTCManagerTests.cs` | WebRTCManager unit tests |
| `SignalingClientTests.cs` | `PolicyRecordingBot.Tests/Signaling/SignalingClientTests.cs` | SignalingClient unit tests |

### Python Files (signaling/)

| File | Source Path | Purpose |
|------|-------------|---------|
| `signaling_server.py` | `signaling/src/signaling_server.py` | WebSocket signaling server |
| `test_signaling_server.py` | `signaling/tests/test_signaling_server.py` | Signaling server tests |
| `conftest.py` | `signaling/tests/conftest.py` | pytest fixtures |
| `requirements.txt` | `signaling/requirements.txt` | Production dependencies |
| `requirements-dev.txt` | `signaling/requirements-dev.txt` | Development dependencies |

### Documentation

Copy all `.md` files from the WebRTC and Signaling directories for reference.

---

## Creating New Project Files

### TeamsMediaBot.Core.csproj

Create `src/TeamsMediaBot.Core/TeamsMediaBot.Core.csproj`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" />

  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
    <ProjectGuid>{YOUR-NEW-GUID-HERE}</ProjectGuid>
    <OutputType>Library</OutputType>
    <RootNamespace>TeamsMediaBot.Core</RootNamespace>
    <AssemblyName>TeamsMediaBot.Core</AssemblyName>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
    <FileAlignment>512</FileAlignment>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)|$(Platform)' == 'Debug|x64'">
    <DebugSymbols>true</DebugSymbols>
    <OutputPath>bin\x64\Debug\</OutputPath>
    <DefineConstants>DEBUG;TRACE</DefineConstants>
    <DebugType>full</DebugType>
    <PlatformTarget>x64</PlatformTarget>
    <ErrorReport>prompt</ErrorReport>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)|$(Platform)' == 'Release|x64'">
    <OutputPath>bin\x64\Release\</OutputPath>
    <DefineConstants>TRACE</DefineConstants>
    <Optimize>true</Optimize>
    <DebugType>pdbonly</DebugType>
    <PlatformTarget>x64</PlatformTarget>
    <ErrorReport>prompt</ErrorReport>
  </PropertyGroup>

  <ItemGroup>
    <!-- WebRTC Components -->
    <Compile Include="WebRTC\MediaBridge.cs" />
    <Compile Include="WebRTC\AudioConverter.cs" />
    <Compile Include="WebRTC\AudioFrame.cs" />
    <Compile Include="WebRTC\IAudioConverter.cs" />
    <Compile Include="WebRTC\IWebRTCManager.cs" />
    <Compile Include="WebRTC\WebRTCManager.cs" />

    <!-- Signaling Components -->
    <Compile Include="Signaling\ISignalingClient.cs" />
    <Compile Include="Signaling\SignalingClient.cs" />

    <!-- Properties -->
    <Compile Include="Properties\AssemblyInfo.cs" />
  </ItemGroup>

  <ItemGroup>
    <!-- Core NuGet Packages -->
    <PackageReference Include="Concentus" Version="2.1.1" />
    <PackageReference Include="SIPSorcery" Version="6.0.12" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />

    <!-- Microsoft Graph Communications -->
    <PackageReference Include="Microsoft.Graph.Communications.Calls.Media" Version="1.2.0.3742" />
    <PackageReference Include="Microsoft.Graph.Communications.Common" Version="1.2.0.3742" />
    <PackageReference Include="Microsoft.Graph.Communications.Core" Version="1.2.0.3742" />

    <!-- Skype Media (required for Microsoft.Graph.Communications.Calls.Media) -->
    <PackageReference Include="Microsoft.Skype.Bots.Media" Version="1.20.0.348-alpha" />
  </ItemGroup>

  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project>
```

**Note:** Generate a new GUID for `ProjectGuid` using Visual Studio or online GUID generator.

### TeamsMediaBot.Tests.csproj

Create `src/TeamsMediaBot.Tests/TeamsMediaBot.Tests.csproj`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" />

  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
    <ProjectGuid>{YOUR-NEW-GUID-HERE}</ProjectGuid>
    <OutputType>Library</OutputType>
    <RootNamespace>TeamsMediaBot.Tests</RootNamespace>
    <AssemblyName>TeamsMediaBot.Tests</AssemblyName>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
    <FileAlignment>512</FileAlignment>
    <IsTestProject>true</IsTestProject>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)|$(Platform)' == 'Debug|x64'">
    <DebugSymbols>true</DebugSymbols>
    <OutputPath>bin\x64\Debug\</OutputPath>
    <DefineConstants>DEBUG;TRACE</DefineConstants>
    <DebugType>full</DebugType>
    <PlatformTarget>x64</PlatformTarget>
    <ErrorReport>prompt</ErrorReport>
  </PropertyGroup>

  <ItemGroup>
    <!-- Test Files -->
    <Compile Include="WebRTC\MediaBridgeTests.cs" />
    <Compile Include="WebRTC\WebRTCManagerTests.cs" />
    <Compile Include="Signaling\SignalingClientTests.cs" />
    <Compile Include="Helpers\TestLogger.cs" />
    <Compile Include="Properties\AssemblyInfo.cs" />
  </ItemGroup>

  <ItemGroup>
    <!-- Test Framework -->
    <PackageReference Include="MSTest.TestAdapter" Version="3.0.2" />
    <PackageReference Include="MSTest.TestFramework" Version="3.0.2" />
    <PackageReference Include="Moq" Version="4.18.4" />
    <PackageReference Include="FluentAssertions" Version="6.10.0" />

    <!-- Microsoft Test Platform -->
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.5.0" />
  </ItemGroup>

  <ItemGroup>
    <!-- Project References -->
    <ProjectReference Include="..\TeamsMediaBot.Core\TeamsMediaBot.Core.csproj">
      <Project>{GUID-OF-CORE-PROJECT}</Project>
      <Name>TeamsMediaBot.Core</Name>
    </ProjectReference>
  </ItemGroup>

  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project>
```

### AssemblyInfo.cs Files

Create `src/TeamsMediaBot.Core/Properties/AssemblyInfo.cs`:

```csharp
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("TeamsMediaBot.Core")]
[assembly: AssemblyDescription("Core library for Teams Media Bot with WebRTC integration")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("TeamsMediaBot")]
[assembly: AssemblyCopyright("Copyright © 2025")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]
[assembly: Guid("your-guid-here")]

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
```

Create similar `AssemblyInfo.cs` for the Tests project with appropriate title/description.

### Solution File

Create `src/TeamsMediaBot.sln`:

```
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TeamsMediaBot.Core", "TeamsMediaBot.Core\TeamsMediaBot.Core.csproj", "{CORE-PROJECT-GUID}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TeamsMediaBot.Tests", "TeamsMediaBot.Tests\TeamsMediaBot.Tests.csproj", "{TESTS-PROJECT-GUID}"
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|x64 = Debug|x64
		Release|x64 = Release|x64
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{CORE-PROJECT-GUID}.Debug|x64.ActiveCfg = Debug|x64
		{CORE-PROJECT-GUID}.Debug|x64.Build.0 = Debug|x64
		{CORE-PROJECT-GUID}.Release|x64.ActiveCfg = Release|x64
		{CORE-PROJECT-GUID}.Release|x64.Build.0 = Release|x64
		{TESTS-PROJECT-GUID}.Debug|x64.ActiveCfg = Debug|x64
		{TESTS-PROJECT-GUID}.Debug|x64.Build.0 = Debug|x64
		{TESTS-PROJECT-GUID}.Release|x64.ActiveCfg = Release|x64
		{TESTS-PROJECT-GUID}.Release|x64.Build.0 = Release|x64
	EndGlobalSection
EndGlobal
```

### TestLogger.cs Helper

Create `src/TeamsMediaBot.Tests/Helpers/TestLogger.cs`:

```csharp
using Microsoft.Graph.Communications.Common.Telemetry;
using System;

namespace TeamsMediaBot.Tests.Helpers
{
    /// <summary>
    /// Simple test logger implementation for unit tests.
    /// </summary>
    public class TestLogger : IGraphLogger
    {
        private readonly string component;

        public TestLogger(string component)
        {
            this.component = component;
        }

        public void Error(Exception exception, string message = null)
        {
            Console.WriteLine($"[ERROR] [{component}] {message}: {exception?.Message}");
        }

        public void Info(string message)
        {
            Console.WriteLine($"[INFO] [{component}] {message}");
        }

        public void Warn(string message)
        {
            Console.WriteLine($"[WARN] [{component}] {message}");
        }

        public void Verbose(string message)
        {
            Console.WriteLine($"[VERBOSE] [{component}] {message}");
        }

        // Implement other IGraphLogger methods as no-ops or console writes
        public IGraphLogger CreateShim(string component, string logComponentSuffix = null)
        {
            return new TestLogger($"{this.component}.{component}");
        }
    }
}
```

---

## Path and Namespace Changes

After copying files, you'll need to update namespaces and using statements.

### Namespace Changes

Replace all occurrences:

**Old:** `Sample.PolicyRecordingBot.FrontEnd`
**New:** `TeamsMediaBot.Core`

**Old:** `Sample.PolicyRecordingBot.Tests`
**New:** `TeamsMediaBot.Tests`

### Files to Update

**Core Library Files:**
```csharp
// Update in all .cs files in src/TeamsMediaBot.Core/
namespace TeamsMediaBot.Core.WebRTC
{
    // ...
}

namespace TeamsMediaBot.Core.Signaling
{
    // ...
}
```

**Test Files:**
```csharp
// Update in all .cs files in src/TeamsMediaBot.Tests/
using TeamsMediaBot.Core.WebRTC;
using TeamsMediaBot.Core.Signaling;
using TeamsMediaBot.Tests.Helpers;

namespace TeamsMediaBot.Tests.WebRTC
{
    // ...
}
```

### Using Statements to Add

Ensure these using statements are present where needed:

```csharp
// For Graph Communications
using Microsoft.Graph.Communications.Common.Telemetry;
using Microsoft.Graph.Communications.Common;

// For SIPSorcery (WebRTC)
using SIPSorcery.Net;
using SIPSorcery;

// For Concentus (Opus)
using Concentus.Enums;
using Concentus.Structs;

// For JSON
using Newtonsoft.Json;

// For WebSocket
using System.Net.WebSockets;
```

---

## Dependencies

### NuGet Package Dependencies

#### Core Library (TeamsMediaBot.Core)

| Package | Version | Purpose |
|---------|---------|---------|
| `Concentus` | 2.1.1 | Pure C# Opus codec for audio compression |
| `SIPSorcery` | 6.0.12 | WebRTC stack (peer connections, RTP, ICE, DTLS) |
| `Newtonsoft.Json` | 13.0.3 | JSON serialization for signaling messages |
| `Microsoft.Graph.Communications.Calls.Media` | 1.2.0.3742 | Teams media handling (IGraphLogger, etc.) |
| `Microsoft.Graph.Communications.Common` | 1.2.0.3742 | Common utilities (ArgumentVerifier) |
| `Microsoft.Graph.Communications.Core` | 1.2.0.3742 | Core Graph Communications features |
| `Microsoft.Skype.Bots.Media` | 1.20.0.348-alpha | Skype media library (required by Graph) |

#### Test Library (TeamsMediaBot.Tests)

| Package | Version | Purpose |
|---------|---------|---------|
| `MSTest.TestFramework` | 3.0.2 | Unit test framework |
| `MSTest.TestAdapter` | 3.0.2 | Test adapter for Visual Studio/dotnet test |
| `Moq` | 4.18.4 | Mocking framework for unit tests |
| `FluentAssertions` | 6.10.0 | Fluent assertion library (optional but recommended) |
| `Microsoft.NET.Test.Sdk` | 17.5.0 | Test platform SDK |

### Python Dependencies (signaling/)

**Production (`requirements.txt`):**
```
websockets==12.0
```

**Development (`requirements-dev.txt`):**
```
pytest==7.4.3
pytest-asyncio==0.21.1
pytest-timeout==2.2.0
```

### External Services

- **COTURN Server** - For TURN/STUN in production (NAT traversal)
  - Can use public STUN servers for testing
  - Example: `stun:stun.l.google.com:19302`

---

## Building the Project

### Using Visual Studio

1. Open `src/TeamsMediaBot.sln` in Visual Studio
2. Select **Build > Build Solution** (Ctrl+Shift+B)
3. Verify no compilation errors
4. Output: `src/TeamsMediaBot.Core/bin/x64/Debug/TeamsMediaBot.Core.dll`

### Using MSBuild (Command Line)

```bash
cd src

# Restore NuGet packages
nuget restore TeamsMediaBot.sln

# Build solution
msbuild TeamsMediaBot.sln /p:Configuration=Debug /p:Platform=x64

# Or build specific project
msbuild TeamsMediaBot.Core/TeamsMediaBot.Core.csproj /p:Configuration=Debug /p:Platform=x64
```

### Using .NET CLI (if converted to SDK-style project)

```bash
cd src
dotnet restore
dotnet build --configuration Debug
```

### Common Build Issues

**Issue:** Missing Microsoft.Skype.Bots.Media NuGet package

**Solution:** This is an alpha package. Add NuGet source:
```bash
nuget sources Add -Name "Microsoft Graph" -Source "https://api.nuget.org/v3/index.json"
```

**Issue:** Platform target mismatch (x86 vs x64)

**Solution:** Ensure all projects target x64:
```xml
<PlatformTarget>x64</PlatformTarget>
```

**Issue:** Missing IGraphLogger or ArgumentVerifier

**Solution:** Add reference to `Microsoft.Graph.Communications.Common` NuGet package.

---

## Testing

### Running C# Unit Tests

#### Using Visual Studio

1. Open **Test Explorer** (Test > Test Explorer)
2. Click **Run All** or select specific tests
3. View results in Test Explorer window

#### Using Command Line

```bash
cd src

# Run all tests
dotnet test TeamsMediaBot.Tests/TeamsMediaBot.Tests.csproj

# Run specific test class
dotnet test --filter "FullyQualifiedName~MediaBridgeTests"

# Run with detailed output
dotnet test --verbosity detailed

# Generate code coverage (requires coverlet)
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

### Running Python Tests

```bash
cd signaling

# Install dependencies
pip install -r requirements.txt
pip install -r requirements-dev.txt

# Run all tests
pytest

# Run with coverage
pytest --cov=src --cov-report=html

# Run specific test file
pytest tests/test_signaling_server.py

# Run with verbose output
pytest -v

# Run specific test
pytest tests/test_signaling_server.py::test_handle_join_call
```

### Test Coverage

**C# Tests:**
- ✅ MediaBridge audio queuing
- ✅ MediaBridge frame dropping
- ✅ WebRTCManager connection lifecycle
- ✅ WebRTCManager audio sending
- ✅ SignalingClient connection/reconnection
- ✅ SignalingClient message handling
- ⏳ End-to-end integration tests (marked with `[Ignore]`)

**Python Tests:**
- ✅ Signaling server connection handling (13/13 tests passing)
- ✅ Join call message handling
- ✅ SDP offer/answer exchange
- ✅ ICE candidate forwarding
- ✅ Multiple client support
- ✅ Error handling

---

## Configuration

### appsettings.json (for Bot Sample)

Create `src/TeamsMediaBot.Sample/appsettings.json`:

```json
{
  "Bot": {
    "AppId": "your-azure-app-id",
    "AppSecret": "your-azure-app-secret",
    "BotBaseUrl": "https://your-bot-url.azurewebsites.net",
    "PlaceCallEndpointUrl": "https://graph.microsoft.com/v1.0/",
    "MediaPlatformSettings": {
      "MediaPlatformInstanceSettings": {
        "CertificateThumbprint": "your-certificate-thumbprint",
        "InstanceInternalPort": 8445,
        "InstancePublicPort": 12332,
        "ServiceFqdn": "your-service-fqdn.cloudapp.azure.com"
      },
      "ApplicationId": "your-azure-app-id"
    }
  },
  "WebRTC": {
    "SignalingServerUrl": "ws://localhost:8765",
    "TurnServer": {
      "Url": "turn:your-turn-server:3478",
      "Username": "username",
      "Password": "password"
    },
    "StunServer": "stun:stun.l.google.com:19302"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "TeamsMediaBot": "Debug"
    }
  }
}
```

### Signaling Server Configuration

The Python signaling server uses environment variables or defaults:

```bash
# Host to bind to (default: localhost)
export SIGNALING_HOST=0.0.0.0

# Port to listen on (default: 8765)
export SIGNALING_PORT=8765

# Start server
cd signaling
python src/signaling_server.py
```

### COTURN Configuration (Production)

Install and configure COTURN for production NAT traversal:

```bash
# Install COTURN (Ubuntu/Debian)
sudo apt-get install coturn

# Edit /etc/turnserver.conf
listening-port=3478
fingerprint
lt-cred-mech
user=username:password
realm=yourdomain.com
```

Restart COTURN:
```bash
sudo systemctl restart coturn
```

---

## Running the Application

### Step 1: Start Python Signaling Server

```bash
cd signaling
python src/signaling_server.py
```

Expected output:
```
INFO:signaling_server:WebSocket signaling server starting on ws://localhost:8765
```

### Step 2: Test Signaling Server

```bash
# In another terminal, test with wscat (install: npm install -g wscat)
wscat -c ws://localhost:8765

# Send test message
> {"type": "join_call", "callId": "test-call-123"}
```

### Step 3: Initialize MediaBridge in Your Bot

```csharp
using TeamsMediaBot.Core.WebRTC;
using TeamsMediaBot.Core.Signaling;

// In your CallHandler or Bot initialization
public async Task OnCallEstablished(Call call)
{
    // Initialize signaling client
    var signalingClient = new SignalingClient(
        logger: this.logger,
        serverUrl: "ws://localhost:8765"
    );

    await signalingClient.ConnectAsync();

    // Initialize WebRTC manager
    var webRtcManager = new WebRTCManager(
        logger: this.logger,
        signalingClient: signalingClient,
        turnServerUrl: "turn:your-turn-server:3478",
        turnUsername: "username",
        turnPassword: "password"
    );

    // Initialize audio converter
    var audioConverter = new AudioConverter(this.logger);

    // Initialize media bridge
    var mediaBridge = new MediaBridge(
        logger: this.logger,
        webRtcManager: webRtcManager,
        audioConverter: audioConverter
    );

    // Attach to BotMediaStream (receives audio from Teams)
    mediaBridge.AttachBotMediaStream(this.botMediaStream);

    // Create WebRTC connection
    await webRtcManager.CreateConnectionAsync(call.Id);

    // Store for cleanup
    this.mediaBridge = mediaBridge;
}

// When audio arrives from Teams
public void OnAudioMediaReceived(AudioMediaBuffer buffer)
{
    byte[] audioData = new byte[buffer.Length];
    Marshal.Copy(buffer.Data, audioData, 0, (int)buffer.Length);

    // Send to WebRTC via MediaBridge
    this.mediaBridge.SendAudioToWebRTC(
        audioData,
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    );
}

// On call ended
public async Task OnCallEnded()
{
    if (this.mediaBridge != null)
    {
        this.mediaBridge.Dispose();
        this.mediaBridge = null;
    }
}
```

### Step 4: Monitor Logs

Watch for successful WebRTC connection establishment:

```
[INFO] Signaling client connected to ws://localhost:8765
[INFO] Creating WebRTC connection for call: abc-123
[INFO] Sending SDP offer via signaling
[INFO] Received SDP answer from remote peer
[INFO] WebRTC peer connection established
[INFO] Encoded 640 bytes PCM to 98 bytes Opus
[INFO] Sent RTP audio packet: 98 bytes
```

---

## Next Steps

### 1. BotMediaStream Integration

Implement the missing `SendAudioToTeams()` method in `BotMediaStream` to support WebRTC → Teams audio flow:

```csharp
public void SendAudioToTeams(byte[] pcmData)
{
    // Create audio buffer
    var audioFormat = this.audioSocket.AudioFormat;
    var buffer = new AudioSendBuffer(pcmData, audioFormat);

    // Send to Teams
    this.audioSocket.Send(buffer);
}
```

Wire this into the MediaBridge's `OnWebRtcAudioReceived` event handler.

### 2. Enable End-to-End Tests

Remove `[Ignore]` attributes from integration tests once BotMediaStream is updated:

```csharp
// In MediaBridgeTests.cs
[TestMethod]
// [Ignore("Requires BotMediaStream.SendAudioToTeams implementation")]
public async Task OnWebRtcAudioReceived_ValidOpusData_ForwardsToTeams()
{
    // Test WebRTC → Teams audio path
}
```

### 3. GStreamer Integration

Create a GStreamer client that connects to the signaling server and establishes WebRTC peer connection:

```python
# Example GStreamer pipeline
gst-launch-1.0 \
  webrtcbin name=webrtc \
  audiotestsrc ! \
  opusenc ! \
  rtpopuspay ! \
  webrtc.
```

Or use a JavaScript WebRTC client for testing.

### 4. Production Deployment

- Deploy bot to Azure App Service
- Configure Azure Application Gateway for media endpoints
- Set up COTURN server for TURN/STUN
- Deploy signaling server (Docker or VM)
- Configure SSL/TLS certificates
- Set up monitoring and logging (Application Insights)

### 5. Monitoring and Observability

Add instrumentation:
- Application Insights for telemetry
- Health check endpoints
- Metrics for dropped frames, connection failures
- Alerting for degraded performance

### 6. Documentation

Create additional docs:
- `ARCHITECTURE.md` - Detailed architecture diagrams
- `DEPLOYMENT.md` - Production deployment guide
- `TROUBLESHOOTING.md` - Common issues and solutions
- `API.md` - API reference for public interfaces

---

## Additional Resources

### Official Documentation

- [Microsoft Graph Communications SDK](https://github.com/microsoftgraph/microsoft-graph-comms-samples)
- [SIPSorcery WebRTC Examples](https://github.com/sipsorcery-org/sipsorcery/tree/master/examples)
- [Concentus Opus Codec](https://github.com/lostromb/concentus)
- [WebRTC Specification](https://www.w3.org/TR/webrtc/)

### Community Resources

- [Microsoft Teams Developer Community](https://developer.microsoft.com/en-us/microsoft-teams)
- [WebRTC Community](https://webrtc.org/)
- [Stack Overflow - Teams Bots](https://stackoverflow.com/questions/tagged/microsoft-teams)

### Sample Implementations

- Original PolicyRecordingBot sample (this migration source)
- SIPSorcery WebRTC examples
- Teams calling bot samples in Microsoft Graph repository

---

## Support and Contributions

For issues, questions, or contributions:

1. Create an issue in your repository's issue tracker
2. Include detailed logs and error messages
3. Provide reproduction steps
4. Reference relevant documentation

---

## License

Specify your chosen license (MIT, Apache 2.0, etc.) in a `LICENSE` file.

---

**Migration Complete!** 🎉

You now have a standalone Teams Media Bot library with WebRTC integration that can be reused across multiple projects.

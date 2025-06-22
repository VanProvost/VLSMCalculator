# VLSM Calculator

A professional-grade Variable Length Subnet Masking (VLSM) calculator with advanced visualization capabilities, built using WPF and .NET 9. This application provides comprehensive subnet calculation, interactive network diagrams, and subnet tree visualization to help network engineers and students understand VLSM allocation strategies.

![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)
![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)
![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)

## 📁 Project Structure

| Folder | Description | Key Files |
|--------|-------------|-----------|
| [`Models/`](Models/) | Data models and entities | SubnetInfo, NetworkInfo, AddressBlock |
| [`Services/`](Services/) | Business logic and calculations | VLSM & Network calculation services |
| [`Controls/`](Controls/) | Custom UI controls | Network diagram, Subnet tree controls |
| [`Views/`](Views/) | Application windows | Main window XAML and code-behind |
| [`Converters/`](Converters/) | XAML value converters | UI binding converters |

## ✨ Features

### Core Functionality

- **VLSM Calculation**: Efficient subnet allocation based on host requirements
- **Real-time Calculation**: Auto-updating results as you type
- **Comprehensive Subnet Details**: Network address, subnet mask, wildcard mask, host ranges, and broadcast addresses
- **Subnet Optimization**: Automatically sorts requirements by size for optimal address space utilization

### Visualization Components

- **Interactive Network Diagrams**: Draggable subnet nodes with connection lines to central router
- **Subnet Tree Visualization**: Hierarchical tree showing VLSM subdivision process

## 🚀 Quick Start

### Prerequisites

- Windows 10/11
- .NET 9.0 Runtime
- Visual Studio 2022 (for development)

### Installation

#### Option 1: Download Release (Recommended)

1. Download the latest release from the [Releases](../../releases) page
2. Extract the ZIP file
3. Run `VLSMCalculator.exe`

#### Option 2: Build from Source

```powershell
# Clone the repository
git clone https://github.com/vanpr/VLSMCalculator.git
cd VLSMCalculator

# Build the project
dotnet build --configuration Release

# Run the application
dotnet run
```

### Basic Usage

1. **Enter Base Network**: Input your base network in CIDR notation (e.g., `192.168.1.0/24`)
2. **Specify Host Requirements**: Enter comma-separated host counts (e.g., `50,25,10,5`)
3. **View Results**: The application automatically calculates and displays:
   - Subnet details with all network parameters
   - Interactive network diagram
   - Hierarchical subnet tree showing subdivision process

## 📊 Example

**Input:**

- Base Network: `192.168.1.0/24`
- Host Requirements: `50,25,10,5`

**Output:**

- Subnet 1: `192.168.1.0/26` (62 usable hosts for 50 required)
- Subnet 2: `192.168.1.64/27` (30 usable hosts for 25 required)
- Subnet 3: `192.168.1.96/28` (14 usable hosts for 10 required)
- Subnet 4: `192.168.1.112/29` (6 usable hosts for 5 required)

## 🏗️ Architecture

### Key Components

#### VLSM Calculation Engine

- **Algorithm**: Implements efficient VLSM allocation using largest-first strategy
- **Validation**: Comprehensive input validation and error handling
- **Optimization**: Minimizes address space waste through optimal subnet sizing

#### Network Diagram

- **Interactive Elements**: Draggable subnet nodes with physics-based movement
- **Performance Modes**: Automatic switching between detailed and high-performance rendering
- **Visual Hierarchy**: Color-coded subnets with connection lines to central router

#### Subnet Tree

- **Hierarchical Visualization**: Shows complete VLSM subdivision process
- **Node Types**: Distinguished rendering for root, intermediate, and allocated nodes
- **Color Coding**: Visual indicators for different subnet states

## 🛠️ Development

### Technology Stack

- **Framework**: .NET 9.0 with WPF
- **UI Library**: ModernWPF for contemporary styling
- **MVVM**: CommunityToolkit.Mvvm for clean architecture
- **Language Features**: C# 12 with nullable reference types

### Building the Project

```powershell
# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run tests (if available)
dotnet test

# Create release build
dotnet publish -c Release -r win-x64 --self-contained
```

### Key Classes

#### [`VLSMCalculationService`](Services/VLSMCalculationService.cs)

Core VLSM calculation logic with subnet allocation algorithms.

```csharp
public static List<SubnetInfo> GetVLSMAllocation(
    string baseNetwork, 
    int baseCIDR, 
    int[] requirements)
```

#### [`NetworkDiagram`](Controls/NetworkDiagram.cs)

Custom Canvas control providing interactive network visualization.

```csharp
public class NetworkDiagram : Canvas
{
    public ObservableCollection<SubnetInfo>? Subnets { get; set; }
    public string BaseNetwork { get; set; }
}
```

#### [`SubnetTree`](Controls/SubnetTree.cs)

Hierarchical tree control showing VLSM subdivision process.

```csharp
public class SubnetTree : Canvas
{
    public List<SubnetInfo> Subnets { get; set; }
    public string BaseNetwork { get; set; }
}
```

### Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

### Code Style

- Follow C# naming conventions
- Use nullable reference types
- Include XML documentation for public APIs
- Maintain clean separation between UI and business logic

## 🐛 Troubleshooting

### Common Issues

#### Application won't start

- Ensure .NET 9.0 Runtime is installed
- Check Windows version compatibility (Windows 10/11)

#### Calculation errors

- Verify CIDR notation format (e.g., `192.168.1.0/24`)
- Ensure host requirements fit within base network
- Check for negative or zero host counts

#### Performance issues with large networks

- The application automatically switches to high-performance mode for networks with >12 subnets
- Consider breaking large networks into smaller segments

### Debug Mode

Run with debug logging enabled:

```powershell
dotnet run --configuration Debug
```

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- Built with [ModernWPF](https://github.com/Kinnara/ModernWpf) for modern UI styling
- Utilizes [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) for MVVM patterns

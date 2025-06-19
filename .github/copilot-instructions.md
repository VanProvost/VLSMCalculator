# Copilot Instructions for VLSM Calculator

<!-- Use this file to provide workspace-specific custom instructions to Copilot. For more details, visit https://code.visualstudio.com/docs/copilot/copilot-customization#_use-a-githubcopilotinstructionsmd-file -->

## Project Overview
This is a .NET 9.0 WPF application that implements a VLSM (Variable Length Subnet Masking) calculator with modern UI and network diagram visualization.

## Key Features
- VLSM subnet calculation based on host requirements
- Network information display (similar to Linux ipcalc)
- Modern dark theme UI using ModernWpfUI
- Interactive network diagram with resizable panels
- Visual representation of network topology

## Architecture
- **MVVM Pattern**: Uses CommunityToolkit.Mvvm for ViewModels and commands
- **Modern UI**: ModernWpfUI for consistent dark theme and modern controls
- **Separation of Concerns**: Models, ViewModels, Views, Services, and Controls are organized in separate folders

## Code Style Guidelines
- Follow C# naming conventions and best practices
- Use async/await patterns for long-running operations
- Implement INotifyPropertyChanged through CommunityToolkit.Mvvm
- Use dependency injection where appropriate
- Maintain consistent dark theme styling throughout the application

## Core Functionality
The application should replicate the functionality of the original PowerShell scripts:
- Get-VLSMSubnets.ps1: VLSM calculation with optimal subnet allocation
- Get-NetInfo.ps1: Network information calculation and display

## UI/UX Requirements
- Consistent dark mode theme
- Resizable panels for flexible layout
- Modern, clean, and professional appearance
- Interactive network diagram showing subnet relationships
- Real-time calculation updates
- Clear error handling and validation feedback

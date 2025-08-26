#!/bin/bash

# Function to temporarily change target frameworks for building
change_target_frameworks() {
    echo "Temporarily changing target frameworks to net8.0 for building..."
    
    # Change all project files to use net8.0
    find src -name "*.csproj" -exec sed -i 's/<TargetFramework>net10.0<\/TargetFramework>/<TargetFramework>net8.0<\/TargetFramework>/g' {} \;
    
    # Update packages to compatible versions for .NET 8.0
    sed -i 's/Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.0-preview.7.25380.108"/Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.8"/g' Directory.Packages.props
}

# Function to restore target frameworks back to net10.0
restore_target_frameworks() {
    echo "Restoring target frameworks to net10.0..."
    
    # Restore all project files to use net10.0
    find src -name "*.csproj" -exec sed -i 's/<TargetFramework>net8.0<\/TargetFramework>/<TargetFramework>net10.0<\/TargetFramework>/g' {} \;
    find tests -name "*.csproj" -exec sed -i 's/<TargetFramework>net8.0<\/TargetFramework>/<TargetFramework>net10.0<\/TargetFramework>/g' {} \;
    
    # Restore package versions
    sed -i 's/Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.8"/Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.0-preview.7.25380.108"/g' Directory.Packages.props
    sed -i 's/"Microsoft.NET.Test.Sdk" Version="17.10.0"/"Microsoft.NET.Test.Sdk" Version="18.0.0-preview.7.25380.15"/g' Directory.Packages.props
    sed -i 's/"Microsoft.AspNetCore.Mvc.Testing" Version="8.0.8"/"Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0-preview.7.25380.108"/g' Directory.Packages.props
    sed -i 's/"Microsoft.Extensions.Testing.Abstractions" Version="8.8.0"/"Microsoft.Extensions.Testing.Abstractions" Version="9.8.0"/g' Directory.Packages.props
    
    # Restore global.json
    sed -i 's/"version": "8.0.119"/"version": "10.0.100-preview.7.25380.108"/g' global.json
}

if [ "$1" = "build" ]; then
    change_target_frameworks
elif [ "$1" = "restore" ]; then
    restore_target_frameworks
else
    echo "Usage: $0 [build|restore]"
    echo "  build  - Change to .NET 8.0 for building"
    echo "  restore - Restore to .NET 10.0"
fi
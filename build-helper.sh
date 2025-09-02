#!/bin/bash

# Function to temporarily change target frameworks for building
change_target_frameworks() {
    echo "Temporarily changing target frameworks to net8.0 for building..."
    
    # Change all project files to use net8.0
    find src -name "*.csproj" -exec sed -i 's/<TargetFramework>net10.0<\/TargetFramework>/<TargetFramework>net8.0<\/TargetFramework>/g' {} \;
    find tests -name "*.csproj" -exec sed -i 's/<TargetFramework>net10.0<\/TargetFramework>/<TargetFramework>net8.0<\/TargetFramework>/g' {} \;
    
    # Update global.json to use .NET 8.0
    sed -i 's/"version": "10.0.100-preview.7.25380.108"/"version": "8.0.119"/g' global.json
    
    # Update packages to compatible versions for .NET 8.0
    sed -i 's/Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.0-preview.7.25380.108"/Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.8"/g' Directory.Packages.props
    sed -i 's/"Microsoft.NET.Test.Sdk" Version="18.0.0-preview.7.25380.15"/"Microsoft.NET.Test.Sdk" Version="17.10.0"/g' Directory.Packages.props
    sed -i 's/"Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0-preview.7.25380.108"/"Microsoft.AspNetCore.Mvc.Testing" Version="8.0.8"/g' Directory.Packages.props
    sed -i 's/"Microsoft.Extensions.Testing.Abstractions" Version="9.8.0"/"Microsoft.Extensions.Testing.Abstractions" Version="8.8.0"/g' Directory.Packages.props
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
    echo "Configuration changed to .NET 8.0. You can now run 'dotnet build' and 'dotnet test'."
    echo "Remember to run '$0 restore' when done to restore .NET 10.0 settings."
elif [ "$1" = "restore" ]; then
    restore_target_frameworks
    echo "Configuration restored to .NET 10.0."
elif [ "$1" = "quick-test" ]; then
    change_target_frameworks
    dotnet build
    build_result=$?
    if [ $build_result -eq 0 ]; then
        echo "Build successful, running tests..."
        dotnet test --verbosity normal
        test_result=$?
    else
        test_result=$build_result
    fi
    restore_target_frameworks
    exit $test_result
else
    echo "Usage: $0 [build|restore|quick-test]"
    echo "  build      - Change to .NET 8.0 for building"
    echo "  restore    - Restore to .NET 10.0"
    echo "  quick-test - Build and test with .NET 8.0, then restore .NET 10.0"
fi
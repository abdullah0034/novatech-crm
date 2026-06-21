# NovaTech CRM — Order Processing Service

Internal CRM and order management system for NovaTech Solutions.

## Architecture

```
src/
  NovaTechCRM.Api/           ASP.NET Core Web API
  NovaTechCRM.Services/      Business logic layer
  NovaTechCRM.Repositories/  Data access layer (EF Core)
  NovaTechCRM.Domain/        Domain models
tests/
  NovaTechCRM.Tests/         Unit tests (xUnit)
```

## Getting Started

This repository contains the NovaTech CRM source files. Project configuration, credentials, and infrastructure setup are managed separately by the NovaTech platform team and are not included here.

Read the source code, make your fix, and open a PR. Your change will be compiled and verified automatically by the CI pipeline.

## Known Issues

See the internal ticket tracker for open issues. Current sprint tickets are tagged `NOVA-*`.

## Stack

- .NET 8
- ASP.NET Core 8 (Web API)
- Entity Framework Core 8 (SQL Server)
- xUnit + Moq (tests)
- FraudShield SDK (fraud detection — external service)

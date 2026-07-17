# Database objects

SQL objects the application depends on — schema and stored procedures that live
in SQL Server, not in EF Core. Some behaviour (atomic sequences, set-based
queries) is implemented here rather than in C#, so when a bug appears to "come
from the database," this is where to look.

```
Database/
  Tables/            table definitions
  StoredProcedures/  stored procedures called from the repositories
```

The C# repositories reference these by name — e.g.
`InvoiceRepository.GetNextSequenceAsync` calls `dbo.usp_GetNextInvoiceSequence`.

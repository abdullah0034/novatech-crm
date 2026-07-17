-- =============================================================================
-- InvoiceSequences
--
-- One row per year holding the last-issued invoice sequence number. Read and
-- advanced by dbo.usp_GetNextInvoiceSequence.
-- Added in migration v11.
-- =============================================================================
CREATE TABLE dbo.InvoiceSequences
(
    [Year]         INT NOT NULL CONSTRAINT PK_InvoiceSequences PRIMARY KEY,
    SequenceNumber INT NOT NULL CONSTRAINT DF_InvoiceSequences_Seq DEFAULT (0)
);
GO

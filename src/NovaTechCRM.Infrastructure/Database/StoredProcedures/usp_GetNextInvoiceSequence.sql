-- =============================================================================
-- usp_GetNextInvoiceSequence
--
-- Returns the next invoice sequence number for a given year. Invoice numbers are
-- formatted as INV-<year>-<sequence>, e.g. INV-2026-00034.
--
-- Called by: NovaTechCRM.Repositories.InvoiceRepository.GetNextSequenceAsync
-- Backing table: dbo.InvoiceSequences (see Tables/InvoiceSequences.sql)
-- Added in migration v11.
-- =============================================================================
CREATE OR ALTER PROCEDURE dbo.usp_GetNextInvoiceSequence
    @Year INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @next INT;

    -- Current highest sequence for this year, plus one.
    SELECT @next = ISNULL(MAX(SequenceNumber), 0) + 1
    FROM   dbo.InvoiceSequences
    WHERE  [Year] = @Year;

    -- Persist the new high-water mark.
    IF EXISTS (SELECT 1 FROM dbo.InvoiceSequences WHERE [Year] = @Year)
        UPDATE dbo.InvoiceSequences
        SET    SequenceNumber = @next
        WHERE  [Year] = @Year;
    ELSE
        INSERT INTO dbo.InvoiceSequences ([Year], SequenceNumber)
        VALUES (@Year, @next);

    SELECT @next AS NextSequence;
END
GO

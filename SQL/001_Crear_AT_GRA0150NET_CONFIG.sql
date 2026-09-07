USE [ANDREU_COLL];
GO

SET ANSI_NULLS ON;
GO

SET QUOTED_IDENTIFIER ON;
GO

SET XACT_ABORT ON;
GO

/* ============================================================
   GRA0150Net
   Creació de la taula de configuració i càrrega inicial

   Taula:
       dbo.AT_GRA0150NET_CONFIG

   Característiques:
       - Script idempotent.
       - Crea la taula només si no existeix.
       - Crea l'índex únic només si no existeix.
       - Insereix únicament les claus que encara no existeixen.
       - No sobreescriu configuracions existents.
   ============================================================ */

BEGIN TRY
    BEGIN TRANSACTION;

    /* ========================================================
       1. Creació de la taula
       ======================================================== */

    IF OBJECT_ID(N'dbo.AT_GRA0150NET_CONFIG', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.AT_GRA0150NET_CONFIG
        (
            ID int IDENTITY(1,1) NOT NULL,

            CLAVE varchar(100) NOT NULL,

            VALOR nvarchar(500) NOT NULL,

            DESCRIPCION nvarchar(255) NULL,

            ACTIVO bit NOT NULL
                CONSTRAINT DF_AT_GRA0150NET_CONFIG_ACTIVO
                DEFAULT (1),

            FECHA_ALTA datetime NOT NULL
                CONSTRAINT DF_AT_GRA0150NET_CONFIG_FECHA_ALTA
                DEFAULT (GETDATE()),

            FECHA_ACTUALIZACION datetime NULL,

            CONSTRAINT PK_AT_GRA0150NET_CONFIG
                PRIMARY KEY CLUSTERED (ID)
        );

        PRINT 'Taula dbo.AT_GRA0150NET_CONFIG creada correctament.';
    END
    ELSE
    BEGIN
        PRINT 'La taula dbo.AT_GRA0150NET_CONFIG ja existeix.';
    END;


    /* ========================================================
       2. Comprovació prèvia de possibles claus duplicades
       ======================================================== */

    IF EXISTS
    (
        SELECT CLAVE
        FROM dbo.AT_GRA0150NET_CONFIG
        GROUP BY CLAVE
        HAVING COUNT(*) > 1
    )
    BEGIN
        THROW 50001,
              'No es pot crear l''índex únic perquè existeixen claus duplicades a AT_GRA0150NET_CONFIG.',
              1;
    END;


    /* ========================================================
       3. Índex únic de la clau de configuració
       ======================================================== */

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id =
              OBJECT_ID(N'dbo.AT_GRA0150NET_CONFIG')
          AND name =
              N'UQ_AT_GRA0150NET_CONFIG_CLAVE'
    )
    BEGIN
        CREATE UNIQUE NONCLUSTERED INDEX
            UQ_AT_GRA0150NET_CONFIG_CLAVE
        ON dbo.AT_GRA0150NET_CONFIG
        (
            CLAVE
        );

        PRINT 'Índex UQ_AT_GRA0150NET_CONFIG_CLAVE creat correctament.';
    END
    ELSE
    BEGIN
        PRINT 'L''índex UQ_AT_GRA0150NET_CONFIG_CLAVE ja existeix.';
    END;


    /* ========================================================
       4. Configuració inicial
       ======================================================== */

    DECLARE @ConfiguracioInicial TABLE
    (
        CLAVE varchar(100) NOT NULL,
        VALOR nvarchar(500) NOT NULL,
        DESCRIPCION nvarchar(255) NULL,
        ACTIVO bit NOT NULL
    );

    INSERT INTO @ConfiguracioInicial
    (
        CLAVE,
        VALOR,
        DESCRIPCION,
        ACTIVO
    )
    VALUES
    (
        'Recargo_LogActivo',
        N'True',
        N'Activa el log del procés de recàrrec dels albarans de venda.',
        1
    ),
    (
        'Recargo_LogRuta',
        N'C:\Logs\A3ErpLogs\GRA0150Net',
        N'Ruta del log del procés de recàrrec dels albarans de venda.',
        1
    );


    /* ========================================================
       5. Inserció idempotent

       Només s'insereixen les claus que no existeixen.
       Les configuracions existents no es modifiquen.
       ======================================================== */

    INSERT INTO dbo.AT_GRA0150NET_CONFIG
    (
        CLAVE,
        VALOR,
        DESCRIPCION,
        ACTIVO
    )
    SELECT
        C.CLAVE,
        C.VALOR,
        C.DESCRIPCION,
        C.ACTIVO
    FROM @ConfiguracioInicial AS C
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.AT_GRA0150NET_CONFIG AS E
        WHERE E.CLAVE = C.CLAVE
    );

    DECLARE @RegistresInserits int = @@ROWCOUNT;

    PRINT CONCAT(
        'Configuracions noves inserides: ',
        @RegistresInserits
    );


    COMMIT TRANSACTION;

    PRINT 'Configuració de GRA0150Net preparada correctament.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    DECLARE @MissatgeError nvarchar(4000) = ERROR_MESSAGE();
    DECLARE @NumeroError int = ERROR_NUMBER();
    DECLARE @LiniaError int = ERROR_LINE();

    PRINT CONCAT(
        'Error ',
        @NumeroError,
        ' a la línia ',
        @LiniaError,
        ': ',
        @MissatgeError
    );

    THROW;
END CATCH;
GO


/* ============================================================
   6. Verificació final de la configuració
   ============================================================ */

SELECT
    ID,
    CLAVE,
    VALOR,
    DESCRIPCION,
    ACTIVO,
    FECHA_ALTA,
    FECHA_ACTUALIZACION
FROM dbo.AT_GRA0150NET_CONFIG
ORDER BY CLAVE;
GO


/* ============================================================
   7. Verificació dels índexs
   ============================================================ */

SELECT
    i.name AS INDICE,
    i.is_primary_key AS ES_CLAVE_PRIMARIA,
    i.is_unique AS ES_UNICO,
    STRING_AGG(c.name, ', ')
        WITHIN GROUP (ORDER BY ic.key_ordinal) AS COLUMNAS
FROM sys.indexes AS i
INNER JOIN sys.index_columns AS ic
    ON ic.object_id = i.object_id
   AND ic.index_id = i.index_id
INNER JOIN sys.columns AS c
    ON c.object_id = ic.object_id
   AND c.column_id = ic.column_id
WHERE i.object_id =
      OBJECT_ID(N'dbo.AT_GRA0150NET_CONFIG')
  AND i.is_hypothetical = 0
GROUP BY
    i.name,
    i.is_primary_key,
    i.is_unique
ORDER BY
    i.is_primary_key DESC,
    i.name;
GO
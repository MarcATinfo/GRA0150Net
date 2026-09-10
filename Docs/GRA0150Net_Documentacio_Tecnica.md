# GRA0150Net — Documentació tècnica

**Projecte:** GRA0150Net  
**ERP:** a3ERP  
**Tipus:** Extensió COM .NET Framework  
**Data de documentació:** 2026-09-10  
**Repositori:** https://github.com/MarcATinfo/GRA0150Net

---

## 1. Objectiu

`GRA0150Net` és una extensió per a a3ERP que gestiona automàticament un **recàrrec percentual als albarans de venda**.

L'usuari informa un percentatge a la capçalera de l'albarà i la DLL:

- calcula la base del recàrrec excloent `RECARGO` i els articles exempts configurats;
- crea una línia `RECARGO [percentatge] %` si no existeix;
- actualitza `PRCMONEDA` i `DESCLIN` si canvia el percentatge o la base;
- elimina la línia si el percentatge passa a `0`;
- evita duplicats;
- refresca el formulari d'a3ERP després de modificar el document;
- evita interferir amb altres línies legítimes que també puguin utilitzar `CODART = 0`.

Les modificacions dels documents es fan exclusivament mitjançant **Interop.a3ERPActiveX**. No es fan `INSERT`, `UPDATE` ni `DELETE` SQL directes sobre els albarans.

---

## 2. Abast funcional

Document a3ERP:

```text
AV
```

Taula de capçalera:

```text
CABEALBV
```

Camp personalitzat:

```text
AT_PORC_RECARGO
```

Característiques funcionals:

```text
Tipus: decimal
Valor inicial: 0
```

El valor s'informa directament com a percentatge:

```text
5    = 5 %
4,5  = 4,5 %
3,85 = 3,85 %
```

Un valor negatiu es tracta funcionalment com a `0`.

---

## 3. Càlcul del recàrrec

### 3.1 Base

La base és el sumatori de `BASEMONEDA` de totes les línies de l'albarà, excloent:

- la pròpia línia de recàrrec `RECARGO`;
- qualsevol línia amb `CODART` present a `dbo.AT_ARTICULOS_EXENTOS`.

```text
BaseRecargo = SUM(BASEMONEDA)
```

La taula `dbo.AT_ARTICULOS_EXENTOS` és una llista viva mantenible des d'a3ERP. No hi ha codis hardcoded al C#.

Estructura:

```text
CODART      varchar(15) NOT NULL PRIMARY KEY
DESCART     varchar(100) NULL
FECHA_ALTA  datetime NOT NULL
```

`CODART` i `DESCART` mantenen els mateixos tipus que `dbo.ARTICULO`.

Consulta utilitzada:

```sql
SELECT CODART
FROM dbo.AT_ARTICULOS_EXENTOS;
```

Els codis es carreguen en un `HashSet<string>(StringComparer.OrdinalIgnoreCase)` i es normalitzen amb `Trim()`.

Una mateixa referència exempta queda exclosa en totes les línies on aparegui.

La taula es consulta de nou en cada guardat/càlcul. No hi ha cache persistent.

Una taula buida és un resultat vàlid.

Si la lectura falla, primer es prova ADODB, després OleDb, i si totes dues fallen es continua amb un `HashSet` buit. En aquest cas el guardat de l'albarà no es bloqueja i el comportament torna a ser l'anterior: només s'exclou `RECARGO`.

### 3.2 Import

```text
ImporteRecargo = BaseRecargo × PorcentajeRecargo / 100
```

Exemple:

```text
Línia 1 BASEMONEDA = 100,00
Línia 2 BASEMONEDA = 50,00

BaseRecargo = 150,00
PorcentajeRecargo = 5

ImporteRecargo = 150 × 5 / 100 = 7,50
```

### 3.3 Decimals

El nombre de decimals es llegeix de:

```text
dbo.DATOSCONFIG.NUMDECPRC
```

En l'entorn validat:

```text
NUMDECPRC = 4
```

L'arrodoniment final es fa amb `decimal` i `MidpointRounding.AwayFromZero`.

Si no es pot recuperar `NUMDECPRC`, s'utilitzen **4 decimals** com a valor de reserva.

---

## 4. Línia de recàrrec

La línia automàtica utilitza:

```text
CODART    = 0
DESCLIN   = RECARGO [percentatge] %
UNIDADES  = 1
PRCMONEDA = import calculat
```

Exemples:

```text
RECARGO 6 %
RECARGO 4,5 %
RECARGO 4,7525 %
```

El percentatge es mostra amb coma decimal i sense zeros sobrants.

Quan es modifica `AT_PORC_RECARGO`, la DLL actualitza:

- `PRCMONEDA`;
- `DESCLIN`.

### Identificació segura

No es considera recàrrec qualsevol línia amb `CODART = 0`.

La identificació requereix:

```text
CODART = 0
+
DESCLIN = RECARGO

o

DESCLIN comença per RECARGO seguit d'un espai
```

Això és important perquè poden existir línies legítimes amb `CODART = 0` i una altra descripció.

Durant les proves es va validar, per exemple:

```text
CODART = 0
DESCLIN = prova text
```

Aquesta línia es tracta com una línia normal.

Ja no existeix compatibilitat legacy amb `RECÀRREC`.

---

## 5. Regles funcionals

```text
Percentatge > 0 + no existeix RECARGO  -> Crear
Percentatge > 0 + existeix RECARGO     -> Actualitzar
Percentatge = 0 + existeix RECARGO     -> Eliminar
Percentatge = 0 + no existeix RECARGO  -> No fer res
```

Si la base calculada és `0`, no es crea cap línia encara que el percentatge sigui superior a zero.

Si ja existia `RECARGO` i el nou import calculat passa a zero, la línia s'elimina per evitar deixar un import antic obsolet.

---

## 6. Flux d'esdeveniments d'a3ERP

Classe COM principal:

```text
GRA0150Net.Principal
```

Procediments exposats:

```text
Iniciar
Finalizar
AntesDeGuardarDocumentoV2
DespuesDeGuardarDocumentoV2
Repintar
UltimoMotivo
```

### AntesDeGuardarDocumentoV2

Responsabilitats principals:

1. comprovar que el document sigui `AV`;
2. ignorar esdeveniments interns provocats per la pròpia DLL;
3. ignorar eliminacions (`Estado = 2`);
4. llegir `AT_PORC_RECARGO`;
5. recuperar les línies;
6. detectar si existeix `RECARGO`;
7. llegir `AT_ARTICULOS_EXENTOS`;
8. calcular `BaseRecargo` excloent `RECARGO` i articles exempts;
9. recuperar `NUMDECPRC`;
10. calcular l'import final;
11. deixar pendent una operació `Crear`, `Actualizar` o `Eliminar`.

No es modifica físicament el document en aquesta fase.

### DespuesDeGuardarDocumentoV2

Després del guardat original d'a3ERP:

1. recupera l'operació pendent;
2. executa la modificació mitjançant ActiveX;
3. protegeix els esdeveniments interns per evitar recursivitat;
4. si l'operació acaba correctament, marca el document per refrescar-lo.

### Repintar

La modificació ActiveX posterior al guardat canvia físicament el mateix albarà que l'usuari continua tenint obert.

Sense refresc, el formulari pot conservar una versió antiga del registre i provocar errors de concurrència com:

```text
Record not found or changed by another user
```

`Repintar("CABEALBV")` retorna `true` només quan GRA0150Net acaba de modificar correctament l'albarà.

Això:

- mostra immediatament la línia `RECARGO`;
- mostra immediatament la seva eliminació;
- sincronitza el formulari amb la BD;
- evita conflictes en guardats posteriors.

---

## 7. Altes noves i IdDoc = 0

En una alta nova s'ha validat:

```text
AntesDeGuardarDocumentoV2
IdDoc = 0
Estado = 0
```

En aquest moment ja hi ha línies, percentatge, base i import, però encara no existeix l'`IDALBV` definitiu.

La DLL conserva una **creació pendent d'alta**.

Després del guardat:

```text
DespuesDeGuardarDocumentoV2
IdDoc = IDALBV definitiu
Estado = 0
```

La DLL associa l'operació pendent amb l'ID definitiu i crea la línia via ActiveX.

Per tant, el recàrrec es crea **en el primer guardat** de l'albarà nou.

---

## 8. Protecció contra recursivitat

Les operacions ActiveX provoquen nous esdeveniments de guardat.

Per evitar que la DLL entri sobre ella mateixa existeix:

```text
A3ErpEventExecutionGuard
```

Quan el guard està actiu:

```text
AntesDeGuardarDocumentoV2   -> ignorat
DespuesDeGuardarDocumentoV2 -> ignorat
```

Això evita bucles i duplicats.

---

## 9. Modificació del document amb ActiveX

Classe:

```text
Infrastructure\ActiveX\AlbaranRecargoActiveXService.cs
```

Operacions principals:

```text
AgregarLineaRecargo
ActualizarLineaRecargo
EliminarLineaRecargo
```

Patró general:

```text
Albaran.Iniciar()
Albaran.Modifica(idAlbaran, false)
...
Albaran.Anade()
Albaran.Acabar()
```

Configuració utilitzada:

```text
OmitirMensajes = true
ValidarPrecios = false
ValidarArtBloqueado = false
AvisarRiesgo = false
```

### Creació

```text
NuevaLineaArt("0", 1)
DESCLIN   = RECARGO [percentatge] %
UNIDADES  = 1
PRCMONEDA = importe
AnadirLinea()
Anade()
```

### Actualització

La línia es localitza mitjançant:

```text
NUMLINALB
```

No s'ha de deduir la línia per la seva posició visual.

Quan s'actualitza la línia, s'informen de nou:

```text
DESCLIN
PRCMONEDA
UNIDADES
```

### Eliminació

```text
BorrarLinea(NUMLINALB)
Anade()
```

No es crida explícitament `CalcularImpuestosyTotales()`. En les proves, la persistència normal d'`Anade()` ha estat suficient.

---

## 10. Connexions d'a3ERP

Classe:

```text
Infrastructure\Runtime\A3ErpRuntimeContext.cs
```

En l'entorn real s'ha comprovat que a3ERP pot proporcionar les connexions de sistema i empresa en ordre invers.

Exemple observat:

```text
BaseDatosEmpresa = ANDREU_COLL
BaseDatosSistema = A3ERP$SISTEMA
ConexionesInvertidas = True
```

Per aquest motiu el codi no confia en el nom dels paràmetres.

Es llegeix `Initial Catalog` / `Database` de les dues cadenes i s'identifica la base de sistema si és `A3ERP$SISTEMA` o acaba en `$SISTEMA`.

No s'han de registrar mai connection strings completes als logs.

Per a lectures auxiliars, la via principal és la connexió viva d'a3ERP:

```text
ADODB.Connection
Enlace.GetConexionDB("EMPRESA")
```

Aquesta connexió es valida amb:

```sql
SELECT DB_NAME()
```

La BD retornada ha de coincidir amb `BaseDatosEmpresa`.

La `ADODB.Connection` és propietat d'a3ERP i no es tanca manualment.

`OleDbConnection` es conserva només com a fallback.

No s'utilitzen credencials hardcoded.

---

## 11. Accés a configuració SQL

Classe:

```text
Infrastructure\Configuration\A3ErpConfiguracionRepository.cs
```

Les lectures intenten utilitzar primer la connexió viva d'a3ERP:

```text
ADODB.Connection
Enlace.GetConexionDB("EMPRESA")
```

Si ADODB no està disponible o la lectura falla, es prova el fallback:

```text
System.Data.OleDb
OleDbConnection
```

No s'utilitza `SqlConnection` per a aquestes lectures.

Lectures principals:

```text
dbo.DATOSCONFIG.NUMDECPRC
dbo.AT_GRA0150NET_CONFIG
dbo.AT_ARTICULOS_EXENTOS
```

El repositori és només de lectura.

En producció ja s'ha validat:

```text
Lectura NUMDECPRC via ADODB correcta.
ConfiguracionDecimalesDisponible=True
```

---

## 12. Configuració pròpia

Taula:

```text
dbo.AT_GRA0150NET_CONFIG
```

Script:

```text
SQL\001_Crear_AT_GRA0150NET_CONFIG.sql
```

Claus actuals:

```text
Recargo_LogActivo
Recargo_LogRuta
```

Exemple:

```text
Recargo_LogActivo = True
Recargo_LogRuta   = C:\Logs\A3ErpLogs\GRA0150Net
```

Taula de manteniment d'articles exempts:

```text
dbo.AT_ARTICULOS_EXENTOS
```

La taula es manté des d'a3ERP i només es consulta `CODART`.

---

## 13. Sistema de logs

Classe:

```text
GRA0150Logger
```

### Log principal

Es configura des de `dbo.AT_GRA0150NET_CONFIG`.

Exemple de ruta:

```text
C:\Logs\A3ErpLogs\GRA0150Net
```

Nom de fitxer:

```text
GRA0150Net_yyyy-MM-dd.log
```

### Log de reserva

Abans de consultar SQL s'inicialitza:

```text
%LOCALAPPDATA%\AT_Infoserveis\GRA0150Net\Logs\
```

El fallback permet registrar problemes si falla la inicialització, SQL, la taula de configuració, la ruta principal o els permisos d'escriptura.

Si el log principal falla durant l'execució, el logger commuta al fallback.

### Diagnòstic del càlcul

El log funcional del càlcul inclou:

```text
NumeroLineasBase
NumeroLineasExcluidas
BaseExcluida
BaseRecargo
```

`NumeroLineasBase` compta només les línies que realment entren al càlcul.

`NumeroLineasExcluidas` compta només les línies excloses per `AT_ARTICULOS_EXENTOS`. La línia `RECARGO` no es compta aquí.

`BaseExcluida` és el `SUM(BASEMONEDA)` de les línies excloses per articles exempts.

El llistat complet de `CODART` exempts només es registra a nivell `DEBUG`, no a `INFO` ni `WARN`.

### Retenció

```text
7 dies
```

S'aplica al log principal i al fallback.

Només s'eliminen fitxers amb el patró:

```text
GRA0150Net_*.log
```

Cap excepció del logger es propaga cap a a3ERP.

---

## 14. Arquitectura del projecte

```text
GRA0150Net
│
├── Domain
│   └── RecargoAlbaranConstants.cs
│
├── Infrastructure
│   ├── ActiveX
│   │   └── AlbaranRecargoActiveXService.cs
│   │
│   ├── Configuration
│   │   └── A3ErpConfiguracionRepository.cs
│   │
│   ├── Connections
│   │   └── A3ErpConexionActivaService.cs
│   │
│   ├── Events
│   │   ├── A3ErpEventDataReader.cs
│   │   └── A3ErpEventExecutionGuard.cs
│   │
│   ├── Logging
│   │   └── GRA0150Logger.cs
│   │
│   └── Runtime
│       ├── A3ErpRuntimeContext.cs
│       └── RecargoAlbaranPendingState.cs
│
├── Services
│   └── RecargoAlbaranService.cs
│
├── SQL
│   └── 001_Crear_AT_GRA0150NET_CONFIG.sql
│
├── Principal.cs
├── GRA0150Net.csproj
├── GRA0150Net.slnx
└── README.md
```

---

## 15. Responsabilitat de les classes

### Principal.cs

Punt d'entrada COM. Coordina context, esdeveniments, càlcul, operacions pendents, ActiveX, `Repintar` i errors.

### RecargoAlbaranService.cs

Lògica funcional: lectura del percentatge, identificació de `RECARGO`, càlcul de base, import i arrodoniment.

### AlbaranRecargoActiveXService.cs

Únic responsable de modificar físicament l'albarà amb ActiveX.

### A3ErpEventDataReader.cs

Interpreta les estructures `object` / `object[]` enviades pels esdeveniments d'a3ERP.

### A3ErpEventExecutionGuard.cs

Evita recursivitat durant els guardats ActiveX interns.

### RecargoAlbaranPendingState.cs

Manté les operacions pendents entre BeforeSave i AfterSave:

```text
Crear
Actualizar
Eliminar
```

També manté temporalment una alta sense ID.

### A3ErpRuntimeContext.cs

Identifica correctament la connexió d'empresa i la de sistema encara que a3ERP les proporcioni invertides.

### A3ErpConexionActivaService.cs

Inicialitza `Enlace`, obté `GetConexionDB("EMPRESA")`, valida la BD activa amb `SELECT DB_NAME()` i exposa la `ADODB.Connection` d'empresa per a lectures auxiliars. No tanca manualment la connexió ADODB.

### A3ErpConfiguracionRepository.cs

Lectures de configuració i taules auxiliars. Utilitza ADODB com a via principal i OleDb com a fallback.

### GRA0150Logger.cs

Log principal configurable, fallback local i retenció de 7 dies.

---

## 16. Tecnologia i compilació

```text
.NET Framework: 4.7.2
Plataforma: x86
Assembly: GRA0150Net
DLL: GRA0150Net.dll
Interop: Interop.a3ERPActiveX
ProgId: GRA0150Net.Principal
GUID: 6F41C93F-65F0-46D2-AE03-A5B4FC5EA150
```

**No canviar el GUID** si no hi ha una necessitat explícita de crear una nova identitat COM.

---

## 17. Desplegament

Ruta prevista:

```text
C:\Program Files (x86)\A3\a3erp\extensiones\AT\GRA0150\Binarios\
```

Elements necessaris:

- `GRA0150Net.dll`;
- diccionari a3ERP amb `AT_PORC_RECARGO`;
- taula `dbo.AT_GRA0150NET_CONFIG`;
- taula `dbo.AT_ARTICULOS_EXENTOS`;
- configuració inicial de logs.

### Primera instal·lació

Registrar en **x86**:

```bat
C:\Windows\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe GRA0150Net.dll /codebase /tlb
```

No utilitzar `Framework64`.

### Actualització d'una versió existent

Si no canvien GUID, ProgId o metadades COM:

1. tancar a3ERP;
2. substituir la DLL;
3. tornar a obrir a3ERP.

No cal tornar a registrar la DLL després de cada compilació.

---

## 18. Desplegament del diccionari

La instal·lació requereix desplegar el diccionari d'a3ERP amb:

```text
Taula: CABEALBV
Camp: AT_PORC_RECARGO
Tipus: decimal
Valor inicial: 0
```

La BD pot contenir físicament el camp però a3ERP no mostrar-lo fins que el diccionari s'hagi actualitzat correctament.

---

## 19. Proves funcionals validades

### Validat en producció

```text
connexió ADODB                               OK
lectura configuració del log                 OK
NUMDECPRC                                    OK
creació RECARGO                              OK
eliminació RECARGO                           OK
protecció de recursivitat                    OK
Repintar                                     OK
DESCLIN "RECARGO [percentatge] %"            OK
arrodoniment a NUMDECPRC                     OK
log principal C:\Logs\A3ErpLogs\GRA0150Net  OK
```

### Validat en local

```text
Alta d'albarà nou amb recàrrec                         OK
Actualització del percentatge                          OK
Crear -> eliminar -> tornar a crear                    OK
Base igual a 0                                         OK
Percentatge > 0 amb base 0                             OK, sense error
CODART=0 que no és RECARGO                             OK
Exclusió de RECARGO de la pròpia base                  OK
Decimals segons NUMDECPRC                              OK
Alta BeforeSave amb IdDoc=0                            OK
Associació amb ID definitiu a AfterSave                OK
Guardat posterior sense conflicte                      OK
Eliminació d'albarà amb Estado=2                       OK
Log local de reserva                                   OK
Exclusió dinàmica via AT_ARTICULOS_EXENTOS             OK
Modificació en calent de la taula sense reiniciar      OK
Article eliminat de la taula torna a BaseRecargo       OK
Article tornat a afegir queda immediatament exclòs     OK
```

`AT_ARTICULOS_EXENTOS` encara no s'ha validat en producció.

Exemples de càlcul validats:

```text
NumeroLineasBase = 2
NumeroLineasExcluidas = 2
BaseExcluida = 350
BaseRecargo = 1500
```

---

## 20. Estats de document observats

```text
Estado = 0 -> Alta
Estado = 1 -> Modificació
Estado = 2 -> Eliminació
```

En una eliminació no es processa el recàrrec perquè a3ERP pot no proporcionar capçalera ni línies completes.

---

## 21. Problemes resolts durant el desenvolupament

### Connexions invertides

A3ERP podia proporcionar sistema i empresa en ordre no fiable. `A3ErpRuntimeContext` les classifica pel nom de la BD.

### SqlConnection incompatible

La cadena rebuda a `Iniciar()` permet identificar la BD, però en producció no sempre és la millor via per obrir lectures auxiliars noves.

La via principal actual és `ADODB.Connection` obtinguda amb `Enlace.GetConexionDB("EMPRESA")` i validada amb `SELECT DB_NAME()`.

`OleDbConnection` es conserva com a fallback.

### ActiveX i Interop

`new AlbaranClass()` provocava problemes amb embedded interop. El patró validat és treballar amb `IAlbaran` i instanciar `new Albaran()`.

### Recursivitat

Les operacions ActiveX disparaven nous esdeveniments. Es va incorporar `A3ErpEventExecutionGuard`.

### Formulari desincronitzat

Després de modificar l'albarà via ActiveX, un segon guardat podia provocar un conflicte de concurrència. Es va resoldre amb `Repintar`.

### Alta nova sense ID

`BeforeSave` arribava amb `IdDoc=0`. `RecargoAlbaranPendingState` conserva la creació pendent fins que `AfterSave` proporciona l'ID definitiu.

### Base = 0

Inicialment es podia programar una creació amb import `0`. Ara, si `ImporteFinal <= 0`, no es crea `RECARGO`; si ja existeix, s'elimina.

---

## 22. Consideracions de manteniment

- No modificar albarans per SQL.
- No identificar `RECARGO` només per `CODART = 0`.
- Identificar `RECARGO` amb `CODART.Trim() = 0` i `DESCLIN = RECARGO` o `DESCLIN` començant per `RECARGO `.
- Mantenir `AT_ARTICULOS_EXENTOS` com a llista viva sense codis hardcoded al C#.
- Utilitzar `NUMLINALB` per editar o eliminar línies amb ActiveX.
- No registrar credencials ni connection strings completes.
- No tancar manualment la `ADODB.Connection` retornada per a3ERP.
- Conservar el fallback de `%LOCALAPPDATA%`.
- Mantenir la separació de responsabilitats entre `Principal`, serveis, ActiveX, runtime, configuració i logging.

---

## 23. Punts pendents / fora d'abast actual

El desenvolupament actual cobreix la generació i manteniment de la línia de recàrrec a l'albarà.

**No s'ha implementat cap adaptació específica de llistats o formats d'impressió.**

Si el client necessita mostrar el percentatge o el recàrrec d'una manera concreta en un llistat, albarà imprès o informe, aquesta part s'ha de revisar separadament.

Tampoc s'ha implementat una distribució específica del recàrrec per múltiples tipus d'IVA. El comportament validat correspon als escenaris provats durant aquest desenvolupament.

---

## 24. Flux resumit

```text
Usuari modifica AV
        |
        v
AntesDeGuardarDocumentoV2
        |
        +--> llegeix AT_PORC_RECARGO
        +--> recupera línies
        +--> exclou RECARGO
        +--> llegeix AT_ARTICULOS_EXENTOS
        +--> exclou articles exempts
        +--> SUM(BASEMONEDA)
        +--> aplica percentatge
        +--> arrodoneix NUMDECPRC
        |
        v
Decideix operació pendent
Crear / Actualizar / Eliminar
        |
        v
a3ERP guarda document
        |
        v
DespuesDeGuardarDocumentoV2
        |
        v
ActiveX modifica RECARGO
        |
        v
Repintar(CABEALBV)
        |
        v
Formulari sincronitzat
```

Alta nova:

```text
BeforeSave IdDoc=0
        |
        v
Alta pendent temporal
        |
        v
a3ERP crea AV
        |
        v
AfterSave amb IDALBV
        |
        v
Crear RECARGO
        |
        v
Repintar
```

---

## 25. Checklist per a modificacions futures

Abans de modificar el projecte, revisar:

- `README.md`;
- aquesta documentació tècnica;
- `Principal.cs`;
- `RecargoAlbaranService.cs`;
- `AlbaranRecargoActiveXService.cs`;
- `RecargoAlbaranPendingState.cs`;
- `A3ErpEventExecutionGuard.cs`;
- `A3ErpRuntimeContext.cs`;
- `A3ErpConfiguracionRepository.cs`;
- `GRA0150Logger.cs`;
- scripts de `SQL`.

Després d'un canvi funcional tornar a validar com a mínim:

```text
Crear RECARGO
Actualitzar RECARGO
Eliminar RECARGO
Alta nova amb IdDoc=0
Base = 0
CODART=0 aliè a RECARGO
AT_ARTICULOS_EXENTOS
Repintar
Recursivitat ActiveX
Log principal
Fallback
```

---

## 26. Referències ràpides

```text
Projecte              GRA0150Net
Document a3ERP         AV
Taula capçalera        CABEALBV
Camp percentatge       AT_PORC_RECARGO
Camp base línia        BASEMONEDA
Article recàrrec       0
Concepte               RECARGO [percentatge] %
Unitats                1
Preu línia             PRCMONEDA
Decimals               DATOSCONFIG.NUMDECPRC
Taula configuració     AT_GRA0150NET_CONFIG
Taula exempcions       AT_ARTICULOS_EXENTOS
Clau log actiu         Recargo_LogActivo
Clau ruta log          Recargo_LogRuta
Retenció logs          7 dies
Framework              .NET Framework 4.7.2
Plataforma             x86
ProgId                 GRA0150Net.Principal
GUID                    6F41C93F-65F0-46D2-AE03-A5B4FC5EA150
```

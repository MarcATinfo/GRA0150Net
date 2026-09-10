\# GRA0150Net



Extensió .NET per a a3ERP que calcula i gestiona automàticament un recàrrec percentual als albarans de venda.



\## Objectiu



El desenvolupament permet informar un percentatge de recàrrec a la capçalera d'un albarà de venda.



A partir d'aquest percentatge, GRA0150Net:



\- calcula la base del recàrrec excloent `RECARGO` i els articles exempts configurats;

\- crea automàticament una línia `RECARGO [percentatge] %`;

\- actualitza `PRCMONEDA` i `DESCLIN` si canvia el percentatge;

\- elimina la línia si el percentatge passa a `0`;

\- evita duplicats;

\- refresca el formulari d'a3ERP després de modificar el document.



\---



\## Camp personalitzat



Taula:



```text

CABEALBV

```



Camp:



```text

AT\_PORC\_RECARGO

```



Característiques:



```text

Tipus: decimal

Valor inicial: 0

```



El valor s'introdueix com a percentatge:



```text

5    = 5 %

4,5  = 4,5 %

3,85 = 3,85 %

```



\---



\## Càlcul del recàrrec



La base es calcula sumant el camp:



```text

BASEMONEDA

```



de totes les línies de l'albarà, excloent:



\- la pròpia línia de recàrrec `RECARGO`;

\- qualsevol línia amb `CODART` present a `dbo.AT_ARTICULOS_EXENTOS`.



Fórmula:



```text

BaseRecargo = SUM(BASEMONEDA)



ImporteRecargo =

&#x20;   BaseRecargo × AT\_PORC\_RECARGO / 100

```



La taula `dbo.AT_ARTICULOS_EXENTOS` és una llista viva mantenible des d'a3ERP.
Una mateixa referència exempta queda exclosa en totes les línies on aparegui.
La taula es consulta de nou en cada guardat/càlcul; no hi ha cache persistent.



Exemple:



```text

Línia 1 BASEMONEDA = 100,00

Línia 2 BASEMONEDA = 50,00



BaseRecargo = 150,00



AT\_PORC\_RECARGO = 5



ImporteRecargo = 150 × 5 / 100

&#x20;              = 7,50

```



L'import final s'arrodoneix segons la configuració d'a3ERP:



```text

DATOSCONFIG.NUMDECPRC

```



\---



\## Línia de recàrrec



La línia creada automàticament utilitza:



```text

CODART    = 0

DESCLIN   = RECARGO [percentatge] %

UNIDADES  = 1

PRCMONEDA = import calculat

```



Exemples de `DESCLIN`:



```text

RECARGO 6 %

RECARGO 4,5 %

RECARGO 4,7525 %

```



El percentatge es mostra amb coma decimal i sense zeros sobrants.
Quan es modifica `AT_PORC_RECARGO`, s'actualitzen tant `PRCMONEDA` com `DESCLIN`.



Per identificar una línia de recàrrec no és suficient que:



```text

CODART = 0

```



També ha de complir:



```text

DESCLIN = RECARGO

o

DESCLIN comença per RECARGO seguit d'un espai

```



Això permet que existeixin altres línies amb `CODART=0` sense interferir amb el desenvolupament.



Ja no existeix compatibilitat legacy amb `RECÀRREC`.



\---



\## Comportament funcional



\### Percentatge superior a 0



Si no existeix una línia `RECARGO`:



```text

Crear

```



Si ja existeix:



```text

Actualitzar

```



\### Percentatge igual a 0



Si existeix `RECARGO`:



```text

Eliminar

```



Si no existeix:



```text

No fer res

```



\### Base igual a 0



Si:



```text

BaseRecargo = 0

```



no es crea cap línia de recàrrec encara que el percentatge sigui superior a `0`.



\---



\## Integració amb a3ERP



Les modificacions dels documents es realitzen exclusivament mitjançant:



```text

Interop.a3ERPActiveX

```



No es realitzen modificacions SQL directes sobre els documents d'a3ERP.



Esdeveniments principals:



```text

AntesDeGuardarDocumentoV2

DespuesDeGuardarDocumentoV2

Repintar

```



`AntesDeGuardarDocumentoV2` calcula el recàrrec i determina l'operació que cal realitzar.



`DespuesDeGuardarDocumentoV2` executa la creació, actualització o eliminació mitjançant ActiveX.



`Repintar` força a3ERP a recarregar el document després de la modificació. Això permet mostrar immediatament la línia `RECARGO` i evita que el formulari mantingui una versió obsoleta del document.



\---



\## Altes noves



En crear un albarà nou, a3ERP envia inicialment:



```text

BeforeSave

IdDoc = 0

Estado = 0

```



GRA0150Net conserva temporalment el recàrrec calculat.



Després del primer guardat, a3ERP proporciona l'identificador definitiu:



```text

AfterSave

IdDoc = IDALBV

Estado = 0

```



En aquest moment GRA0150Net associa l'operació pendent amb l'ID definitiu i crea la línia `RECARGO`.



Per tant, el recàrrec queda creat en el mateix primer guardat de l'albarà.



\---



\## Configuració SQL



Les lectures auxiliars utilitzen com a via principal la connexió viva d'a3ERP:



```text

ADODB.Connection

Enlace.GetConexionDB("EMPRESA")

```



La connexió ADODB es valida amb:



```text

SELECT DB_NAME()

```



La base de dades retornada ha de coincidir amb `BaseDatosEmpresa`.
`OleDbConnection` queda només com a fallback.
No s'utilitzen credencials hardcoded, no es registren connection strings i la `ADODB.Connection` no es tanca manualment.



La configuració pròpia del desenvolupament es troba a:



```text

dbo.AT\_GRA0150NET\_CONFIG

```



Script de creació:



```text

SQL\\001\_Crear\_AT\_GRA0150NET\_CONFIG.sql

```



Configuració actual del log:



```text

Recargo\_LogActivo

Recargo\_LogRuta

```



Exemple:



```text

Recargo\_LogActivo = True

Recargo\_LogRuta   = C:\\Logs\\A3ErpLogs\\GRA0150Net

```



\### Articles exempts



La llista d'articles que no formen part de la base del recàrrec es manté a:



```text

dbo.AT_ARTICULOS_EXENTOS

```



Estructura:



```text

CODART      varchar(15) NOT NULL PRIMARY KEY

DESCART     varchar(100) NULL

FECHA_ALTA  datetime NOT NULL

```



`CODART` i `DESCART` mantenen els mateixos tipus que `dbo.ARTICULO`.
La taula és l'única font de veritat; no hi ha codis hardcoded al C#.



La DLL consulta:



```text

SELECT CODART

FROM dbo.AT_ARTICULOS_EXENTOS;

```



Els codis es carreguen en un `HashSet<string>(StringComparer.OrdinalIgnoreCase)` i es normalitzen amb `Trim()`.
Una taula buida és un resultat vàlid.



Si la lectura falla, primer es prova ADODB, després OleDb, i finalment es continua amb un `HashSet` buit.
En aquest cas el guardat no es bloqueja i el comportament torna a ser l'anterior: només s'exclou `RECARGO`.



\---



\## Logs



\### Log principal



El log funcional es configura des de:



```text

dbo.AT\_GRA0150NET\_CONFIG

```



mitjançant les claus:



```text

Recargo\_LogActivo

Recargo\_LogRuta

```



Fitxer diari:



```text

GRA0150Net\_yyyy-MM-dd.log

```



Exemple:



```text

GRA0150Net\_2026-09-07.log

```



\### Log de reserva



Abans de consultar la configuració SQL, GRA0150Net inicialitza un log local de reserva.



Ruta:



```text

%LOCALAPPDATA%\\AT\_Infoserveis\\GRA0150Net\\Logs\\

```



Aquest log permet registrar incidències encara que:



\- falli la connexió;

\- no existeixi la configuració SQL;

\- la ruta principal no sigui accessible;

\- existeixin problemes de permisos o escriptura.



Si la ruta principal falla durant l'execució, el logger commuta automàticament al log local de reserva.



\### Diagnòstic del càlcul



El log del càlcul inclou:



```text

NumeroLineasBase

NumeroLineasExcluidas

BaseExcluida

BaseRecargo

```



`NumeroLineasBase` compta només les línies que realment entren al càlcul.
`NumeroLineasExcluidas` compta només les línies excloses per `AT_ARTICULOS_EXENTOS`; la línia `RECARGO` no es compta aquí.
`BaseExcluida` és el `SUM(BASEMONEDA)` de les línies excloses per articles exempts.



El llistat complet de `CODART` exempts només es registra a nivell `DEBUG`.



\### Retenció



Es conserven només els logs dels últims:



```text

7 dies

```



tant al log principal com al log de reserva.



Només s'eliminen fitxers propis amb el patró:



```text

GRA0150Net\_\*.log

```



\---



\## Arquitectura



Estructura principal del projecte:



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

│   ├── 001\_Crear\_AT\_GRA0150NET\_CONFIG.sql

│   └── taula dbo.AT_ARTICULOS_EXENTOS mantinguda des d'a3ERP

│

└── Principal.cs

```



\---



\## Compilació



Projecte:



```text

.NET Framework 4.7.2

x86

```



DLL generada:



```text

GRA0150Net.dll

```



ProgId COM:



```text

GRA0150Net.Principal

```



GUID COM:



```text

6F41C93F-65F0-46D2-AE03-A5B4FC5EA150

```



\---



\## Desplegament



Ruta prevista de la DLL:



```text

C:\\Program Files (x86)\\A3\\a3erp\\extensiones\\AT\\GRA0150\\Binarios\\

```



El desplegament requereix:



1\. desplegar el diccionari d'a3ERP amb el camp `AT\_PORC\_RECARGO`;

2\. crear la taula `dbo.AT\_GRA0150NET\_CONFIG`;

3\. crear o validar la taula `dbo.AT_ARTICULOS_EXENTOS`;

4\. configurar `Recargo\_LogActivo` i `Recargo\_LogRuta`;

5\. copiar `GRA0150Net.dll`;

6\. registrar la DLL en la primera instal·lació;

7\. reiniciar a3ERP.



Per registrar la DLL en entorn x86:



```bat

C:\\Windows\\Microsoft.NET\\Framework\\v4.0.30319\\RegAsm.exe GRA0150Net.dll /codebase /tlb

```



Si només s'actualitza el codi i no han canviat el GUID, el ProgId o les metadades COM, normalment és suficient substituir la DLL amb a3ERP tancat.



\---



\## Proves validades



VALIDAT EN PRODUCCIÓ:



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



VALIDAT EN LOCAL:



```text

Alta d'albarà nou amb recàrrec                         OK

Actualització del percentatge                          OK

Crear -> eliminar -> tornar a crear                    OK

Base igual a 0                                         OK

CODART=0 que no és RECARGO                             OK

Exclusió de RECARGO de la seva pròpia base             OK

Exclusió dinàmica via AT_ARTICULOS_EXENTOS             OK

Modificació en calent de la taula sense reiniciar      OK

Article eliminat de la taula torna a BaseRecargo       OK

Article tornat a afegir queda immediatament exclòs     OK

Log local de reserva                                   OK

Retenció de logs de 7 dies                             OK

```



`AT_ARTICULOS_EXENTOS` encara no s'ha validat en producció.



\---



\## Manteniment



No s'han de modificar directament per SQL els albarans d'a3ERP.



Qualsevol modificació dels documents s'ha de realitzar mitjançant les APIs proporcionades per a3ERP.



La identificació actual de la línia de recàrrec és:



```text

CODART.Trim() = 0

\+

DESCLIN = RECARGO

o

DESCLIN comença per RECARGO seguit d'un espai

```



Aquesta comprovació és important perquè poden existir altres línies legítimes amb:



```text

CODART = 0

```



que no corresponen al recàrrec.



Ja no existeix compatibilitat legacy amb `RECÀRREC`.



\---



\## Projecte



```text

Desenvolupament: GRA0150

DLL: GRA0150Net

ERP: a3ERP

```


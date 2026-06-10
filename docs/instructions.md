# Instructions

<!-- Add your instructions here -->
# Richiesta Integrazione Dati Telematici BMW (BMW CarData)

Ciao! Vorrrei configurare l'integrazione della mia vettura (BMW 535i) per poter leggere i dati telematici in tempo reale (chilometraggio, scadenze manutenzione CBS, ecc.) ed evitare i blocchi di sicurezza dei browser (CORS). 

Abbiamo analizzato la documentazione ufficiale e i dati estratti dai server di Monaco. Ecco la situazione attuale e i dettagli tecnici per procedere:

## 1. Lo Stato Attuale (Il problema del Libretto Digitale vuoto)
Abbiamo scaricato l'archivio telematico ufficiale (`Telematics Archive` / `Digital Service Booklet`) direttamente dal portale MyBMW. 
* **Problema:** Nel database centrale di BMW risulta registrato un unico intervento: il *"Controllo di preconsegna"* a **0 km** effettuato l'11.04.2014 alla consegna del veicolo.
* **Conseguenza:** Non è possibile ricostruire lo storico dei tagliandi passati tramite i server ufficiali (probabilmente perché effettuati da officine indipendenti non connesse al portale BMW AOS).

## 2. Obiettivo: Lettura Dati Correnti (CBS & Chilometri)
Visto che lo storico passato è vuoto, l'obiettivo è leggere i **dati attuali dei servizi attivi (Condition Based Service)** e la telemetria live. Per farlo dobbiamo interfacciarci con le API ufficiali di BMW CarData.

### Riferimenti Documentazione Ufficiale BMW:
https://bmw-cardata.bmwgroup.com/customer/public/api-documentation
* **Specifiche API REST (Pull):** https://bmw-cardata.bmwgroup.com/customer/public/api-specification
* **Documentazione Stream MQTT (Push):** https://bmw-cardata.bmwgroup.com/customer/public/api-documentation/Id-Streaming

## 3. Cosa dobbiamo fare lato Portale BMW Developer / CarData:
Dobbiamo configurare l'accesso per utenti privati che, come da documentazione, utilizza il flusso **OAuth 2.0 Device Authorization Grant (Device Code Flow)** anziché il classico *Client Secret*:

1. **Creazione Client:** Creare un nuovo *CarData Client* sul portale per ottenere il `Client ID`.
2. **Abilitazione Scope:** Richiedere esplicitamente l'accesso a:
   * `cardata:api:read` (per le chiamate REST GET).
   * `cardata:streaming:read` (per lo streaming live MQTT sulla porta 8883).
3. **Selezione Data Package:** Nella sezione *Data Selection*, cliccare su "Load more" e selezionare manualmente i pacchetti relativi a **Condition Based Service (CBS)** e **Mileage**. Se non vengono spuntati qui, i JSON di risposta ometteranno i dati.

## 4. Architettura Richiesta per l'Integrazione:
Per testare ed esporre i dati aggirando i limiti del browser, l'idea è procedere in uno di questi due modi:
* **Opzione C#:** Implementare un piccolo client in C# (usando `HttpClient` per le REST API e `MQTTnet` per lo streaming telematico) che gestisca il Device Code Flow per scambiare l'8-digit code di BMW con l' `access_token` (e il relativo `id_token` per MQTT).
* **Opzione Tool Desktop/Proxy:** Utilizzare un client MQTT nativo (come *MQTT X* o *Node-RED*) configurando l'endpoint `stream.api-cardata.bmwgroup.com:8883` e iscrivendosi al topic `customers/vehicles/WBA5H31080D223259/stream`.

Definiamo insieme quale strada pipeline è più comoda per iniziare a mappare i JSON!
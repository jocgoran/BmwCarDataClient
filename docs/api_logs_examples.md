# BMW CarData API — Request and Response Logs (Steps 5, 6, and 7)

This document provides a highly detailed specification of the network logs, request structures, and response payloads for **Step 5** (Smart Maintenance Tyre Diagnosis), **Step 6** (Deploy Subscription Containers), and **Step 7** (Live Data Telemetry Stream). 

All endpoints, headers, payload formats, and schemas correspond directly to the classes implemented in the `BmwCarDataClient` application (`BmwRestService.cs`, `BmwMqttService.cs`, and `BmwContainerManager.cs`).

---

## 5. Smart Maintenance Tyre Diagnosis (REST API)

### 📌 Purpose
Requests real-time and predictive tire wear, pressure anomalies, temperature status, and maintenance recommendations for a specific vehicle.

### 🌐 HTTP Request
* **Method:** `GET`
* **URL:** `https://api-cardata.bmwgroup.com/customers/vehicles/{vin}/smartMaintenanceTyreDiagnosis`
* **HTTP Protocol:** `HTTP/1.1`

#### Headers
```http
Authorization: Bearer eyJhbGciOiJSUzI1NiIsImtpZCI6IjF... [Valid Access Token]
Accept: application/json
x-version: v1
User-Agent: BmwCarDataClient/1.0
Host: api-cardata.bmwgroup.com
```

---

### 📥 HTTP Response (Success)
* **Status Code:** `200 OK`
* **Content-Type:** `application/json; charset=utf-8`

#### Response Body JSON
```json
{
  "vin": "WBA5H31080D223259",
  "timestamp": "2026-05-30T21:30:15.123Z",
  "tyreDiagnosis": {
    "overallStatus": "ATTENTION_REQUIRED",
    "recommendation": "Check pressure on front-right tyre and schedule tread depth inspection.",
    "tyres": [
      {
        "position": "FRONT_LEFT",
        "pressure": {
          "currentBar": 2.4,
          "recommendedBar": 2.5,
          "status": "OK"
        },
        "temperature": {
          "currentCelsius": 22.5,
          "status": "OK"
        },
        "treadDepth": {
          "estimatedRemainingMm": 4.8,
          "status": "OK"
        },
        "anomalyDetected": false
      },
      {
        "position": "FRONT_RIGHT",
        "pressure": {
          "currentBar": 2.0,
          "recommendedBar": 2.5,
          "status": "LOW_PRESSURE_WARNING"
        },
        "temperature": {
          "currentCelsius": 23.1,
          "status": "OK"
        },
        "treadDepth": {
          "estimatedRemainingMm": 4.5,
          "status": "OK"
        },
        "anomalyDetected": true
      },
      {
        "position": "REAR_LEFT",
        "pressure": {
          "currentBar": 2.7,
          "recommendedBar": 2.7,
          "status": "OK"
        },
        "temperature": {
          "currentCelsius": 21.8,
          "status": "OK"
        },
        "treadDepth": {
          "estimatedRemainingMm": 5.2,
          "status": "OK"
        },
        "anomalyDetected": false
      },
      {
        "position": "REAR_RIGHT",
        "pressure": {
          "currentBar": 2.7,
          "recommendedBar": 2.7,
          "status": "OK"
        },
        "temperature": {
          "currentCelsius": 22.0,
          "status": "OK"
        },
        "treadDepth": {
          "estimatedRemainingMm": 3.1,
          "status": "WEAR_WARNING"
        },
        "anomalyDetected": true
      }
    ]
  }
}
```

---

### ❌ HTTP Response (Error Scenarios)

#### 400 Bad Request
Occurs if the VIN format is invalid or request parameters are corrupted.
* **Status Code:** `400 Bad Request`
```json
{
  "errorCode": "INVALID_VIN_FORMAT",
  "message": "The provided VIN WBA5H31080D223259xxx is malformed or invalid.",
  "timestamp": "2026-05-30T21:30:16.002Z"
}
```

#### 401 Unauthorized
Occurs if the access token has expired or is invalid.
* **Status Code:** `401 Unauthorized`
```json
{
  "errorCode": "AUTHENTICATION_FAILED",
  "message": "Token signature validation failed or token is expired.",
  "timestamp": "2026-05-30T21:30:17.412Z"
}
```

#### 403 Forbidden
Occurs if the user hasn't accepted terms of service or doesn't have permissions for the smart maintenance resource.
* **Status Code:** `403 Forbidden`
```json
{
  "errorCode": "ACCESS_FORBIDDEN",
  "message": "The user has not authorized resource access to 'smartMaintenanceTyreDiagnosis'.",
  "timestamp": "2026-05-30T21:30:18.199Z"
}
```

---
---

## 6. Deploy Subscription Containers (REST API)

### 📌 Purpose
Provision 10 virtual container resource configurations on the BMW backend, corresponding to the application's subscription levels (Cabin, Drivetrain, Powertrain, etc.), mapping descriptors imported from `StreamingData.csv`.

### 🌐 HTTP Request
* **Method:** `POST`
* **URL:** `https://api-cardata.bmwgroup.com/customers/containers`
* **HTTP Protocol:** `HTTP/1.1`

#### Headers
```http
Authorization: Bearer eyJhbGciOiJSUzI1NiIsImtpZCI6IjF... [Valid Access Token]
Accept: application/json
x-version: v1
Content-Type: application/json; charset=utf-8
User-Agent: BmwCarDataClient/1.0
Host: api-cardata.bmwgroup.com
```

#### Request Payload Templates (JSON)

Here is a sample payload representing the **"Cabin"** container created by mapping descriptors:

```json
{
  "name": "Cabin",
  "purpose": "App Subscription Tier - Cabin",
  "technicalDescriptors": [
    "vehicle?.cabin?.climatization?.preconditioningStatus",
    "vehicle?.cabin?.door?.driverDoorLockStatus",
    "vehicle?.cabin?.door?.passengerDoorLockStatus",
    "vehicle?.cabin?.window?.driverWindowPositionStatus",
    "vehicle?.cabin?.sunroof?.sunroofPositionStatus"
  ]
}
```

Here is a sample payload representing the **"Drivetrain"** container:

```json
{
  "name": "Drivetrain",
  "purpose": "App Subscription Tier - Drivetrain",
  "technicalDescriptors": [
    "vehicle?.drivetrain?.transmission?.gearPosition",
    "vehicle?.drivetrain?.engine?.rpm",
    "vehicle?.drivetrain?.battery?.stateOfCharge",
    "vehicle?.drivetrain?.battery?.rangeElectricKm"
  ]
}
```

---

### 📥 HTTP Response (Success - Created)
* **Status Code:** `201 Created`
* **Content-Type:** `application/json; charset=utf-8`

#### Response Body JSON
```json
{
  "containerId": "cnt-8f4ba62b-92ca-49d7-84bc-299f1165a2cc",
  "name": "Cabin",
  "purpose": "App Subscription Tier - Cabin",
  "technicalDescriptors": [
    "vehicle?.cabin?.climatization?.preconditioningStatus",
    "vehicle?.cabin?.door?.driverDoorLockStatus",
    "vehicle?.cabin?.door?.passengerDoorLockStatus",
    "vehicle?.cabin?.window?.driverWindowPositionStatus",
    "vehicle?.cabin?.sunroof?.sunroofPositionStatus"
  ],
  "creationTimestamp": "2026-05-30T21:35:44.789Z",
  "status": "ACTIVE"
}
```

---

### ❌ HTTP Response (Error Scenarios)

#### 409 Conflict (Container already exists)
Occurs if a container with the same name is already defined.
* **Status Code:** `409 Conflict`
```json
{
  "errorCode": "CONTAINER_ALREADY_EXISTS",
  "message": "A container named 'Cabin' already exists for this client account.",
  "timestamp": "2026-05-30T21:35:46.002Z"
}
```

#### 422 Unprocessable Entity
Occurs if any technical descriptors are unsupported or spelled incorrectly.
* **Status Code:** `422 Unprocessable Entity`
```json
{
  "errorCode": "INVALID_DESCRIPTOR",
  "message": "Technical descriptor 'vehicle?.cabin?.door?.unknownDescriptor' is not recognized by CarData directory.",
  "timestamp": "2026-05-30T21:35:47.199Z"
}
```

---
---

## 7. Live Data Telemetry Stream (MQTT Protocol)

### 📌 Purpose
Connect to BMW’s high-frequency telemetry stream server over MQTT (Message Queuing Telemetry Transport) secured via TLS to capture push events of vehicle telematics state changes.

### 🔌 Connection Parameters

* **MQTT Protocol Version:** `MQTT v5.0` (Highly optimized for cloud telematics, fallback to `v3.1.1`)
* **Transport:** `TCP + TLS 1.2/1.3`
* **Broker Hostname:** `customer.streaming-cardata.bmwgroup.com`
* **Port:** `9000` (Secure WebSocket/TLS)
* **Clean Session:** `true`
* **Keep Alive:** `60 seconds`
* **Client ID Format:** `BmwCarDataClient-{random_8_chars}` (e.g., `BmwCarDataClient-a3b4c5d6`)

#### Credentials Authentication Details
* **Username (GCID):** `e540c289-3966-48c3-8cbe-f461f4dcd9c7`
* **Password:** JWT token from authorization flow.
  * *Primary attempt:* The ID Token (`id_token`).
  * *Fallback attempt:* The Access Token (`access_token`).

---

### 📡 Subscription Packet
* **QoS Level:** `0` (At most once / Fire-and-forget for telemetry streaming updates)
* **Target Topic:** `customers/vehicles/{vin}/stream`
  * *Example:* `customers/vehicles/WBA5H31080D223259/stream`

---

### 📥 Sample Telemetry Event Message (Payload)
Upon a state change, a structured telemetry JSON frame is published.

* **Topic:** `customers/vehicles/WBA5H31080D223259/stream`
* **Format:** JSON UTF-8

#### Payload JSON (Example Update)
```json
{
  "vin": "WBA5H31080D223259",
  "publishTime": "2026-05-30T21:40:02.991Z",
  "sequenceNumber": 14502,
  "telemetry": {
    "vehicle": {
      "cabin": {
        "climatization": {
          "preconditioningStatus": "ACTIVE"
        },
        "door": {
          "driverDoorLockStatus": "LOCKED",
          "passengerDoorLockStatus": "LOCKED"
        }
      },
      "drivetrain": {
        "engine": {
          "rpm": 1850.0
        },
        "transmission": {
          "gearPosition": "D"
        },
        "battery": {
          "stateOfCharge": 84,
          "rangeElectricKm": 312
        }
      },
      "status": {
        "odometerKm": 24890.3,
        "speedKph": 95.5
      },
      "isMoving": true
    }
  }
}
```

---

## Summary of Status Codes and Troubleshooting

| Endpoint / Action | Expected Success Code | Common Error Codes | Meaning & Fix |
|:---|:---|:---|:---|
| **Step 5: Get Tyre Diagnosis** | `200 OK` | `401 Unauthorized` | Access token expired. Call Step 3 Refresh. |
| | | `403 Forbidden` | Vehicle owner has not accepted Cardata agreement. |
| | | `404 Not Found` | The VIN is invalid or not registered to the client. |
| **Step 6: Deploy Containers** | `201 Created` | `409 Conflict` | Container already exists. Clear/rename container name. |
| | | `422 Unprocessable` | Contains invalid descriptors. Check `StreamingData.csv`. |
| **Step 7: Connect Stream** | `MqttConnectResult.Success` | Socket Disconnection | Wrong GCID/ID Token, or network blocking port `9000`. |

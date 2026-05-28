# Changelog

## 1.0.0 - Unreleased

## 0.32.0 - 2026-05-11

### Added

- Add `SqlServer-SingleDatabaseInstance` to SQL-Server DataPort
- Add `ConnectTimeout` option to SQL DataPorts
- Add `DisableStructureValidation` option to SQL DataPorts
- Add `SqlConnectionPoolTracker` for SQL-Server DataPort testing on windows
- Add Quote Always Strategy for database part names for SQL DataPorts

### Changed

- Change `Json Column` and `Json Object` Icons to `folder` in SQL DataPorts
- Rename SQL-Server `Instance` to `Connection`

### Fixed

- NotNull is now true when a column is a PrimaryKey or AutoIncrement in SQL DataPorts
- HTTP Client now retries on failure
- HTTP Server now sends error code on post failure

## 0.31.0 - 2026-04-22

### Fixed

- Set HTTP content type according to content
- Swap and fix PostgeSql schema and database icon
- Fix OPC UA server icon

## 0.30.0 - 2026-03-20

### Added

- Add `Name` property to `DataPortCommunication`
- Implement `Http Server` data port
- Introduce `ViciOne.Suite.DataPort.Extensions` package with reusable base classes and utilities for implementing custom data ports:
  - Base templates: `IncomingDataPortBase` for read-oriented data ports with periodic polling, `OutgoingDataPortBase` for write-oriented data ports with queue processing and retry logic
  - Infrastructure components: `PeriodicJob` for periodic task execution with Polly resilience, `TypedNodeMapper` for configuration mapping with FluentValidation integration
  - Queue system: `ChannelQueue` and `PrependableChannelQueue` with configurable overflow strategies (drop oldest/newest, throw exception)
  - Resilience utilities: `RetryBackoffCalculator` for exponential/linear backoff with jitter, retry policies (`BoundedRetryPolicy`, `InfiniteRetryPolicy`)
  - Value validation: `DataPointValueValidator` with all-or-nothing semantics
  - Data port abstractions: `IClientLifecycleManager`, `IReadClient`, `IWriteClient`, `IDataPoint`, `IPollingDataPoint`, `IDataPointPollingGroup`, `IDataPointValue`, `IRetryPolicy`
  - Structured exception types: `ConnectionFailureException`, `DataRetrievalException`, `DataConversionException`, `InvalidConfigurationException`, `MaxRetriesExceededException`, `QueueBackpressureException`
  - Mapper abstractions: `IDataPointsMapper` (outgoing) and `IDataPointGroupsMapper` (incoming) for decoupled configuration-to-data-point mapping
  - Value conversion abstractions: `IExternalValueConverter` (outgoing: `ExternalValue` → `TDataPointValue`) and `IDataPointValueConverter` (incoming: `TDataPointValue` → `ExternalValue`)
  - Configuration verification: `IDataPointConfigurationVerifier` for async pre-poll data point validation, replacing the synchronous `IDataPointValidator`
  - Strongly-typed value objects: `DataPointIdentifier`, `DataTypeName`, `QueueSize`
- Introduce `ViciOne.Suite.DataPort.Extensions.Testing` package with specialized test utilities for data port development:
  - Eventual assertions: `EventualAssertions`, `EventualValue<T>`, `EventualValueAssertions<T>`
  - YAML consistency testing: `YamlConsistencyBaseTest` base class for validating consistency between YAML configuration files and C# DataPort implementations
  - Test helpers: `DataPortCommunicationExtensions` for reflection-based Communication analysis, `RulesetExtensions` for querying YAML rulesets
  - Centralized test logging configuration
- Use timing-safe comparison for OPC UA server credential verification
- Add brute-force protection to OPC UA server authentication (lockout after failed attempts with exponential delay)
- Add audit logging for OPC UA server security events (failed/successful authentication, anonymous access, account lockout)
- Warn on insecure OPC UA server configuration (anonymous access, auto-accept untrusted certificates) at startup
- Configure explicit `TransportQuotas` and `ServerConfiguration` session limits for OPC UA server (max message size, max session count, operation timeout, request throttling) to harden against denial-of-service attacks
- Enable OPC UA stack tracing for security and error events
- Add configurable `SecurityPolicy` to OPC UA server (`None`, `Basic256Sha256_Sign`, `Basic256Sha256_SignAndEncrypt`, `Aes256_Sha256_RsaPss_SignAndEncrypt`); default is `Basic256Sha256_SignAndEncrypt` — `MessageSecurityMode.None` is no longer offered unless explicitly selected
- Add `DesignId` property to `DataPortCommunication`

### Changed

- Change MetaData names to full names
- Make OPC UA certificate optional
- **Breaking** (`DataPort.Extensions`): Refactored public API — `IDataPoint`, `IncomingDataPortBase`, `OutgoingDataPortBase`, `IDataPointValidator`, `IDataPointValueFactory`, `QueueConfiguration`, and all sub-namespaces changed; see [DataPort.Extensions breaking changes](docs/DataPort.Extensions/README.md#breaking-changes-in-this-release)
- Update `.NET` to `10.0`
- Implement new icons
- Update `.yaml` files format to `1.0.0` (`ViciOne.TreeBuilder`)
- Update `ViciOne.ManagedEngine.Contracts` to `1.0.0`
- Update `AWSSDK.IoT` to `4.0.6.9`
- Update `Microsoft.Azure.Devices` to `1.41.0`
- Update `Microsoft.Data.Sqlite` to `10.0.5`
- Update `Npgsql` to `10.0.2`
- Update `MySqlConnector` to `2.5.0`
- Update `Microsoft.Data.SqlClient` to `7.0.0`
- Update `InfluxDB.Client` to `5.0.0`

### Fixed

- Fix OPC UA server `TrustedPeerCertificates` not being configured (was incorrectly assigned to `TrustedIssuerCertificates`)
- Update MaxValue for `AuthenticationType` property to allow `Bearer` authentication in HTTP ruleset
- Fix `NotNull` property description in database rulesets (MySql, MariaDb, PostgreSql, SqlServer, Sqlite)
- Add missing symbol to forbidden chars for sql queries
- Improve bearer token evaluation in http data port
- Fix exception exposure in http server data port
- Fix OPC UA server exception on sending the same status code multiple times
- Fix OPC UA server crash on receiving a number while max and min properties are set
- Fix OPC UA server exception on connect when incoming and outgoing have the same nodes
- Fix default port of SQL Server in yaml file

## 0.29.0 - 2025-10-21

### Added

- Support `MaxPendingMessages` and `DisableCertificateValidation` mqtt data port options in other mqtt implementations
- Support configuration of `ReceiveMaximum` in MQTT

### Changed

- **Breaking**: Rename `HTTP` data port to `HTTP Client`
- Update `ViciOne.TreeBuilder` to `0.6.0`
- Implement queues for Azure data ports
- Update `MQTTnet.Extensions` to `0.21.0`
- Update `ViciOne.ManagedEngine.Contracts` to `0.62.0`

### Fixed

- Build MQTT client options on startup to prevent deploy failures

## 0.28.0 - 2025-07-14

### Added

- Support configuration of max pending messages for MQTT
- Support for bearer and basic authentication for `HTTP Client` data port

### Changed

- Update `MQTTnet.Extensions` to `0.19.0`
- Optimize InfluxDb data port memory usage
- Update `AWSSDK.IoT` to `4.0.0.13`
- Update `Azure.Messaging.EventGrid` to `5.0.0`
- Update `Microsoft.Data.Sqlite` to `9.0.7`

## 0.27.0 - 2025-07-04

### Fixed

- Respect nodes of all shared communications in OPC UA server

## 0.26.0 - 2025-05-20

### Changed

- Update `ViciOne.ManagedEngine.Contracts` to `0.61.0`
- Update `AWSSDK.IoT` to `4.0.0.2`
- Update `Azure.Messaging.EventGrid` to `4.30.0`
- Update `Microsoft.Data.SqlClient` to `6.0.2`
- Update `Microsoft.Data.Sqlite` to `9.0.5`
- Update `OPCFoundation.NetStandard.Opc.Ua.Client` to `1.5.375.457`
- Update `OPCFoundation.NetStandard.Opc.Ua.Server` to `1.5.375.457`

## 0.25.0 - 2025-03-04

### Added

- Provide value handling abstraction for unspecified trees

### Changed

- Make schema obligatory in MySql and MariaDB data ports
- Update `Microsoft.Data.Sqlite` to `9.0.2`
- Update `Microsoft.Extensions.Logging.Abstractions` to `9.0.2`
- Update `MQTTnet.Extensions` to `0.18.0`
- Update `Npgsql` to `9.0.3`
- Update `ViciOne.ManagedEngine.Contracts` to `0.58.0`

### Removed

- Remove status code validation on writing values to OPC UA server data port

### Fixed

- OPC UA Client data port can handle multiple data points for the same direction

## 0.24.0 - 2024-12-17

### Changed

- Update `.NET` to `9.0`
- Update `AWSSDK.IoT` to `3.7.404.8`
- Update `Microsoft.Azure.Devices` to `1.40.0`
- Update `Microsoft.Data.Sqlite` to `9.0.0`
- Update `MySqlConnector` to `2.4.0`
- Update `Npgsql` to `9.0.2`
- Update `OPCFoundation.NetStandard.Opc.Ua.Client` to `1.5.374.158`
- Update `OPCFoundation.NetStandard.Opc.Ua.Server` to `1.5.374.158`
- Update `ViciOne.ManagedEngine.Contracts` to `0.56.0`
- Update `ViciOne.TreeBuilder` to `0.5.0`
- Apply readonly to static nodes in AWS IoT YAML file
- Apply readonly to static nodes in Azure IoT Hub YAML file
- Restrict transfer directions in Azure IoT Hub YAML file

### Fixed

- Do not try to send reported properties with timestamp in Azure IoT Hub data port

## 0.23.0 - 2024-12-11

### Added

- Support cancellation in OPCUA server
- **Breaking**: Support device shadows in AWS IoT data port
- **Breaking**: Create things and attach certificates in AWS IoT data port
- Support incoming side in AWS IoT data port
- **Breaking**: Support device twin in Azure data port
- **Breaking**: Support property categories in YAML files
- Added Azure EventGrid Topic DataPort

### Changed

- Use MQTT client to communicate with AWS IoT
- **Breaking**: Use an interface for type Node
- Update `MQTTnet.Extensions` to `0.17.0`
- Set author of YAML files to company name

### Fixed

- Dispose released OPCUA clients
- **Breaking**: Rework data type mapping for MySQL data port
- Send `GUID` as `string` for SQL-Server data port
- Receive messages without pooling in MQTT data port
- **Breaking**: Send `byte[]` as `varbinary(MAX)` for SQL-Server data port
- Do not throw if MQTT receives a partial JSON

### Removed

- **Breaking**: Remove `IDisposable` from OPCUA server communications

## 0.22.0 - 2024-11-06

### Added

- Support configurable serializer in MQTT data port

### Removed

- Remove `System.Reactive` from `ViciOne.Suite.DataPort.RelationalDatabase`

## 0.21.0 - 2024-11-04

### Changed

- **Breaking**: Rename `InfluxDB` to `InfluxDb`

### Removed

- **Breaking**: Remove ANNA data port

### Fixed

- **Breaking**: Configure relational communication properties correctly in YAML files
- Open SQLite database with mode `ReadWriteCreate`

## 0.20.0 - 2042-10-29

### Changed

- **Breaking**: Rename assembly `ViciOne.Suite.DataPort.MariaDb` to `ViciOne.Suite.DataPort.MySql`

## 0.19.0 - 2024-10-28

### Added

- Make relational communication properties configurable in YAML files
- Support to send `JsonObject` for a JSON node in a group
- Use value type from tree node as fallback in MQTT data port
- Support to pass certificates in Base64 format in MQTT data port

### Changed

- **Breaking**: Remove value-type structure for composed JSON nodes and put the type in a seperate meta token in AWS IoT data port
- **Breaking**: Remove value-type structure for composed JSON nodes and put the type in a seperate meta token in Azure IoT Hub data port
- **Breaking**: Remove value-type structure for composed JSON nodes and put the type in a seperate meta token in HTTP data port
- **Breaking**: Remove value-type structure for composed JSON nodes and put the type in a seperate meta token in InfluxDB data port
- **Breaking**: Remove value-type structure for composed JSON nodes and put the type in a user property in MQTT data port
- **Breaking**: Remove value-type structure for composed JSON nodes and put the type in a seperate meta token in relational data ports

### Fixed

- Make PEM certificates usable in MQTT data port
- Apply correct names to TLS options in MQTT data port
- **Breaking**: Handle `TimeSpan` as `interval` in PosgreSQL
- **Breaking**: Handle `DateTime` as `timestamp without time zone` in PosgreSQL
- **Breaking**: Handle `decimal` as `numeric` in PosgreSQL
- **Breaking**: Add precision to `decimal` in MariaDb
- **Breaking**: Change `Guid` to `UUID` in MariaDb
- **Breaking**: Add precision to `decimal` in SqlServer

### Removed

- **Breaking**: Remove reference handling in JSONs

## 0.18.0 - 2024-10-21

### Added

- Add AWS YAML file
- Add Azure YAML file
- Add InfluxDB YAML file
- Add HTTP YAML file
- Add OPC UA YAML files
- Support different HTTP methods in HTTP incoming
- Support client id in MQTT configuration
- **Breaking**: Support retain flag in MQTT data port
- Support will in MQTT data port
- Support to disable MQTT connection pooling
- Support node property `ReadOnly` in OPC UA server data port
- **Breaking**: Extend OPC UA server configuration
- **Breaking**: Extend OPC UA client configuration

### Changed

- Unwrap AWS exception to display the status code
- Apply current state to SQLite YAML file
- Apply current state to SqlServer YAML file
- Apply current state to MySql YAML file
- Apply current state to MariaDb YAML file
- Apply current state to PostgreSql YAML file
- Use validity valid as default in MQTT incoming
- Use validity valid as default in OPC UA incoming
- Use node type as fallback in MQTT incoming
- Shorten display names of data ports
- Improve OPC UA server data port
- Reuse certificate in OPC UA server data port
- Update `OPCFoundation.NetStandard.Opc.Ua.Server` to `1.5.374.126`
- Replace placeholder icons in YAML files
- Improve OPC UA client data port
- Update `OPCFoundation.NetStandard.Opc.Ua.Client` to `1.5.374.126`
- Fine tune log messages and levels
- Update `ViciOne.ManagedEngine.Contracts` to `0.55.0`
- Update `AWSSDK.IotData` to `3.7.401.5`
- Update `InfluxDB.Client` to `4.18.0`
- Update `Microsoft.Data.Sqlite` to `8.0.10`
- Update `MQTTnet.Extensions` to `0.16.0`
- Update `Npgsql` to `8.0.5`
- Send RawData to ANNA even if no record reason is set
- **Breaking**: Alter communication ID of PostgreSQL
- **Breaking**: Alter type of HTTP PollInterval to `int?`
- Use `PeriodicTimer` in ANNA data port

### Removed

- Remove static root node
- Remove read only node type in OPC UA server data port
- Remove table as child node for PostgreSql Database in yaml file

### Fixed

- Set TransferDirections in YAML according to actual implemented features
- Only create Azure IoT devices for device nodes
- Return an empty list in buffer enumerator if cancellation token is canceled
- Use affected channels instead of node ID in InfluxDB and relational data ports
- Apply default type to json column
- Regard `DisableCertificateValidation` properly in MQTT data port
- **Breaking**: Move timestamp to the correct location in the ANNA json
- Compare communications in OPC UA server instance manager without nodes
- Correct double data type for PostgreSql
- Avoid exceptions when disposing relational databases

## 0.17.0 - 2024-09-05

### Changed

- Allow inconsistent property configuration in MQTT communication

## 0.16.0 - 2024-09-05

### Added

- Support certificate authentication in MQTT communication
- Support session options in MQTT communication

### Changed

- Update `MQTTnet.Extensions` to `0.14.0`
- Move `None` in arrays of intermediate layers to the first position in YAMLs

### Fixed

- Only use MQTT extended features in correct version

## 0.15.0 - 2024-08-30

### Changed

- Update `ViciOne.ManagedEngine.Contracts` to `0.54.0`
- Update `InfluxDB.Client` to `4.17.0`
- Update `AWSSDK.IotData` to `3.7.400.12`
- Update `Microsoft.Data.SqlClient` to `5.2.2`
- Update `Microsoft.Data.Sqlite` to `8.0.8`

## 0.14.1 - 2024-08-23

### Fixed

- Deliver ANNA design file

## 0.14.0 - 2024-07-19

### Added

- Add raw data trigger information to ANNA model
- Deliver ANNA design file

### Changed

- **Breaking**: Adjust ANNA contracts to the existing structure
- **Breaking**: Rename namespace `ViciOne.Suite.DataPort.Contracts` to `ViciOne.DataPort.Anna.Contracts`
- Update InfluxDB.Client to 4.16.0
- Update AWSSDK.IotData to 3.7.300.118
- Update Microsoft.Data.Sqlite to 8.0.7
- Update MQTTnet.Extensions to 0.13.0
- Update ViciOne.ManagedEngine.Contracts to 0.53.0

### Removed

- Remove unused meta data properties from ANNA contracts

## 0.13.0 - 2024-06-19

### Added

- Add typed and well-named properties to MQTT communication
- Add typed and well-named properties to MariaDb communication
- Add typed and well-named properties to PostgreSql communication
- Add typed and well-named properties to Sqlite communication
- Add typed and well-named properties to SqlServer communication
- Assign ID to each communication

### Changed

- Update AWSSDK.IotData to 3.7.300.105
- Update InfluxDB.Client to 4.15.0
- Update Microsoft.Data.SqlClient to 5.2.1
- Update Microsoft.Data.Sqlite to 8.0.6
- Update MQTTnet.Extensions to 0.12.0
- Update MySqlConnector to 2.3.7
- Update Npgsql to 8.0.3
- Update System.Reactive to 6.0.1
- Update ViciOne.ManagedEngine.Contracts to 0.52.0

### Removed

- **Breaking:** Support specifying supported DataPort designs for DataPort communication
- **Breaking:** Support properties for DataPort communication

## 0.12.0 - 2024-04-17

### Added

- Provide SQLite pooling property
- Deliver design files in the packages

### Changed

- Remove Polly from RelationalDatabase
- Remove Polly from InfluxDb
- Update AWSSDK.IotData to 3.7.300.75
- Update Microsoft.Azure.Devices.Client to 1.42.3
- Update Microsoft.Data.SqlClient to 5.2.0
- Update Microsoft.Data.Sqlite to 8.0.4
- Update MySqlConnector to 2.3.6
- Update ViciOne.ManagedEngine.Contracts to 0.49.0

## 0.11.0 - 2024-02-22

### Fixed

- Map channels instead of nodes to columns in relational database abstraction

## 0.10.0 - 2024-02-21

### Changed

- **Breaking:** Encapsulate DataPort property in its own type

## 0.9.0 - 2024-02-19

### Added

- Support MariaDb, MySql, Postgres, SQLite, SqlServer DataPort designs

## 0.8.0 - 2024-02-16

### Added

- Support specifying supported DataPort designs for DataPort communication
- Support MQTT DataPort design
- Support properties for DataPort communication

### Changed

- Rename design ids to readable names
- Update AWSSDK.IotData to 3.7.300.50
- Update Microsoft.Data.SqlClient to 5.1.5
- Update Microsoft.Data.Sqlite to 8.0.2
- Update MySqlConnector to 2.3.5
- Update Npgsql to 8.0.2
- Update Polly to 8.3.0
- Update ViciOne.ManagedEngine.Contracts to 0.48.0

### Removed

- **Breaking:** Remove typed and well-named properties from MQTT communication
- **Breaking:** Remove typed and well-named properties from MariaDb communication
- **Breaking:** Remove typed and well-named properties from PostgreSql communication
- **Breaking:** Remove typed and well-named properties from Sqlite communication
- **Breaking:** Remove typed and well-named properties from SqlServer communication

## 0.7.0 - 2024-01-19

### Added

- Add status code property to OPC UA server
- Provide InfluxDB data port

### Changed

- Update .NET to 8.0
- Update MQTTnet.Extensions to 0.11.0
- Update ViciOne.ManagedEngine.Contracts to 0.47.0
- Update AWSSDK.IotData to 3.7.300.32
- Update Microsoft.Azure.Devices.Client to 1.42.2
- Update MySqlConnector to 2.3.3
- Update Polly to 8.2.1

## 0.6.0 - 2023-12-18

### Added

- Add channels for sending and receiving of node properties
- Provide OPC UA client data port
- Provide MariaDB data port
- Add port to PostgreSQL communication
- Provide OPC UA server data port
- Provide AWS IoT outgoing data port
- Add connection tests to data port

### Changed

- **Breaking:** Rename Azure IoT Hub types
- **Breaking:** Rename AnnaContracts to Anna.Contracts
- **Breaking:** Use type string instead of GUID for design id
- Update Microsoft.Azure.Devices to 1.39.1
- Update Microsoft.Azure.Devices.Client to 1.42.1
- Update Microsoft.Data.Sqlite to 8.0.0
- Update MQTTnet.Extensions to 0.10.0
- Update Polly to 8.2.0
- Update Npgsql to 8.0.1
- Update ViciOne.ManagedEngine.Contracts to 0.46.0

### Fixed

- Avoid using shared array pool

### Removed

- **Breaking:** Remove data port designs

## 0.5.0 - 2023-11-02

### Added

- Provide PostgreSql data port
- Provide incoming HTTP data port
- Provide outgoing Azure IoT Hub data port
- Support configurable HTTP method and headers for HTTP data port

### Changed

- Allow more dynamic configuration of HTTP data port
- Improve handling of edge cases in MQTT data port
- Improve SQL-Server create table command creation
- Update Microsoft.Data.SqlClient to 5.1.2
- Update Microsoft.Data.Sqlite to 7.0.13
- Update Polly to 8.1.0
- Update ViciOne.ManagedEngine.Contracts to 0.45.0

### Fixed

- Make HTTP outgoing data port creatable by managed engine provider
- Handle missing values for columns at insert in relational database data ports

## 0.4.0 - 2023-10-06

### Added

- Provide SQLite data port
- Provide SQL-Server data port
- Provide HTTP post data port
- Provide general relational database abstraction for data ports
- Provide method to create Json structure from nodes
- Add custom properties to nodes

### Changed

- Update MQTTnet.Extensions to 0.9.0
- Use a more specific package of System.Reactive in ANNA data port
- Use type-value structure for composed JSON nodes in MQTT data port
- Dispose MQTT communications
- Update ViciOne.ManagedEngine.Contracts to 0.44.0

## 0.3.0 - 2023-08-03

### Added

- Support MQTT protocol version 3.1.1 and 5.0

### Changed

- Update MQTTnet.Extensions to 0.8.0
- Update ViciOne.ManagedEngine.Contracts to 0.43.0

## 0.2.0 - 2023-06-29

### Added

- Provide ANNA data port

### Changed

- Update MQTTnet.Extensions to 0.7.0
- Update ViciOne.ManagedEngine.Contracts to 0.42.0
- Use JsonDerivedType attribute instead of TypeNameHandlingConverter
- Update ViciOne.Core.Runtime to 0.36.0

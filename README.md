# RFID Gateway

Serviço intermediário que conecta o leitor RFID Impinj ao backend do sistema de estacionamento. Detecta leituras de tags RFID e publica eventos de entrada/saída via HTTP.

## Stack

- **Runtime**: .NET 10.0
- **Framework**: ASP.NET Core
- **SDK do leitor**: Impinj OctaneSDK 5.2.0

## Quickstart

### Pré-requisitos

- .NET 10.0 SDK
- Leitor RFID Impinj acessível na rede

Clone o repositório e navegue até o diretório do gateway:

```bash
cd gateway
```

### Configuração

Edite o arquivo `Gateway.API/appsettings.json` com os dados do ambiente:

```json
{
  "Reader": {
    "Hostname": "nome-ou-ip-do-leitor",
    "AntennaIds": [1, 2],
    "TagCooldownSeconds": 45
  },
  "Gateway": {
    "Domain": "endereco-para-enviar-dados",
    "Endpoint": "rota-para-envio"
  }
}
```

| Chave | Descrição | Padrão |
| :--- | :--- | :--- |
| `Reader:Hostname` | Hostname ou IP do leitor Impinj | — (obrigatório) |
| `Reader:AntennaIds` | Portas de antena a ativar (vazio = todas) | `[]` |
| `Reader:TagCooldownSeconds` | Intervalo mínimo entre eventos da mesma tag | `30` |
| `Reader:Session` | Sessão RFID (0–3) | `2` |
| `Reader:TagPopulationEstimate` | Estimativa de tags simultâneas | `32` |
| `Gateway:Domain` | URL base do backend | — |
| `Gateway:Endpoint` | Rota de destino dos eventos | `api/accesses` |

> **Atenção:** a antena de porta `1` é tratada como **entrada**; as demais como **saída**.

### Rodando localmente

```bash
dotnet run --project Gateway.API
```

A API estará disponível em `http://localhost:5001`.

## Endpoints

| Método | Rota | Descrição |
| :--- | :--- | :--- |
| `GET` | `/status` | Status de conexão do leitor e estado de cada antena |
| `GET` | `/antennas` | Lista todas as antenas com potência, sensibilidade e conexão |
| `GET` | `/antennas/{port}` | Dados de uma antena específica |
| `PUT` | `/antennas/{port}` | Atualiza potência (`power`) e/ou sensibilidade (`sensitivity`) |

## Testes

Os testes do gateway são organizados em dois projetos: `Gateway.Tests.Unit` e `Gateway.Tests.Integration`. Ambos utilizam `xUnit` como framework de teste.

### Testes unitários

```bash
dotnet test Gateway.Tests.Unit
```

### Testes de integração

> O projeto de integração está estruturado, mas sem testes implementados.

```bash
dotnet test Gateway.Tests.Integration
```

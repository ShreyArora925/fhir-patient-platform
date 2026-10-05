# Infrastructure

Bicep templates for the FHIR patient platform's Azure resources.

`main.bicep` provisions:

- A Service Bus namespace (Basic tier) named `sb-fhirplatform-<uniqueString(resourceGroup().id)>`.
- A queue `hl7-inbound` (max delivery count 5, lock duration 1 minute, dead-lettering on expiry).

## Deploy (PowerShell)

```powershell
az login
az group create --name rg-fhir-platform --location canadacentral
az deployment group create `
  --resource-group rg-fhir-platform `
  --template-file infra/main.bicep `
  --parameters infra/main.parameters.json
```

## Get the connection string

```powershell
$ns = az deployment group show --resource-group rg-fhir-platform --name main `
  --query properties.outputs.serviceBusNamespaceName.value --output tsv
az servicebus namespace authorization-rule keys list `
  --resource-group rg-fhir-platform `
  --namespace-name $ns `
  --name RootManageSharedAccessKey `
  --query primaryConnectionString --output tsv
```

`RootManageSharedAccessKey` has full Manage/Send/Listen rights. Keep the connection string out of source control
(use user secrets locally and Key Vault or app settings in Azure).

## Tear down

```powershell
az group delete --name rg-fhir-platform --yes --no-wait
```

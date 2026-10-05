// Azure Service Bus for inbound HL7 v2 messages.
// Deploy into an existing resource group: az deployment group create -g <rg> -f main.bicep -p main.parameters.json

@description('Azure region for all resources. Defaults to the resource group location.')
param location string = resourceGroup().location

@description('Name of the queue that receives raw HL7 v2 messages.')
param queueName string = 'hl7-inbound'

var namespaceName = 'sb-fhirplatform-${uniqueString(resourceGroup().id)}'

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: namespaceName
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    minimumTlsVersion: '1.2'
  }
}

resource hl7InboundQueue 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: serviceBusNamespace
  name: queueName
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    deadLetteringOnMessageExpiration: true
  }
}

output serviceBusNamespaceName string = serviceBusNamespace.name
output queueName string = hl7InboundQueue.name

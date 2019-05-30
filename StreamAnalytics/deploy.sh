#!/bin/bash

# Variables
resourceGroup="AADEventsResourceGroup"
location="westeurope"
subscriptionId=$(az account show --query id --output tsv)
template="./azuredeploy.json"
parameters="./azuredeploy.parameters.json"

# check if the resource group already exists
echo "Checking if ["$resourceGroup"] resource group actually exists in the ["$subscriptionId"] subscription..."

az group show --name $resourceGroup &> /dev/null

if [[ $? != 0 ]]; then
	echo "No ["$resourceGroup"] resource group actually exists in the ["$subscriptionId"] subscription"
    echo "Creating ["$resourceGroup"] resource group in the ["$subscriptionId"] subscription..."
    
    # create the resource group
    az group create --name $resourceGroup --location $location 1> /dev/null
        
    if [[ $? == 0 ]]; then
        echo "["$resourceGroup"] resource group successfully created in the ["$subscriptionId"] subscription"
    else
        echo "Failed to create ["$resourceGroup"] resource group in the ["$subscriptionId"] subscription"
        exit
    fi
else
	echo "["$resourceGroup"] resource group already exists in the ["$subscriptionId"] subscription"
fi

# validate template
az group deployment validate \
--resource-group $resourceGroup \
--template-file $template \
 --parameters $parameters 1> /dev/null

if [[ $? == 0 ]]; then
    echo "["$template"] ARM template successfully validated"
else
    echo "Failed to validate the ["$template"] ARM template"
    exit
fi

# deploy template
az group deployment create \
--resource-group $resourceGroup \
--template-file $template \
 --parameters $parameters 1> /dev/null

 if [[ $? == 0 ]]; then
    echo "["$template"] ARM template successfully provisioned"
else
    echo "Failed to provision the ["$template"] ARM template"
    exit
fi

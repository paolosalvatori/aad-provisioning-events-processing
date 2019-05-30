#!/bin/bash

# Variables
location="westeurope"
templates=("./resourceGroup.json" "./functionApp.json")
parametersFiles=("./resourceGroup.values.json" "./functionApp.values.json")

# Validate ARM templates
for i in ${!templates[@]}
do
	template="${templates[$i]}"
	parameters="${parametersFiles[$i]}"
    
	echo "Validating $template..."
	# validate template
	az deployment validate \
	--location $location \
	--template-file $template \
	--parameters $parameters 1> /dev/null

	if [[ $? == 0 ]]; then
		echo "["$template"] ARM template successfully validated"
	else
		echo "Failed to validate the ["$template"] ARM template"
		exit
	fi
done

# Proceed to deploy ARM templates only if all of them are valid
for i in ${!templates[@]}
do
	template="${templates[$i]}"
	parameters="${parametersFiles[$i]}"

	echo "Deploying $template..."
	# deploy template
	az deployment create \
	--location $location \
	--template-file $template \
	--parameters $parameters 1> /dev/null

	 if [[ $? == 0 ]]; then
		echo "["$template"] ARM template successfully provisioned"
	else
		echo "Failed to provision the ["$template"] ARM template"
		exit
	fi
done
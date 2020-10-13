@echo off
REM deploys all Kubernetes services to their staging environment

set namespace=phobos-web
set location=%~dp0/environment
REM datadog key is equal to the first commandline argument
set datadogKey="%~1" 

if "%~1"=="" (
	REM No DataDog key provided.
	echo No DataDog API key has been provided. Can't complete deployment.
	echo Run the script using the following syntax:
    echo 'deployAll.cmd [datadog apiKey]'
    exit 1
) 

echo "Deploying K8s resources from [%location%] into namespace [%namespace%]"

echo "Creating Namespaces..."
kubectl apply -f "%~dp0/namespace.yaml"

echo "Using namespace [%namespace%] going forward..."

echo "Creating configurations from YAML files in [%location%/configs]"
for %%f in (%location%/configs/*.yaml) do (
    echo "Deploying %%~nxf"
    kubectl apply -f "%location%/configs/%%~nxf" -n "%namespace%"
)

echo "Creating environment-specific services from YAML files in [%location%]"
for %%f in (%location%/*.yaml) do (
    echo "Deploying %%~nxf"
    kubectl apply -f "%location%/%%~nxf" -n "%namespace%"
)

echo "Installing DataDog via Helm v3"
REM see https://app.datadoghq.com/signup/agent#kubernetes for instructions
helm install datadog-agent -f "%~dp0/services/datadog-values.yaml" --set datadog.site="datadoghq.com" --set datadog.apiKey=%datadogKey% --namespace "%namespace%" datadog/datadog 

echo "Creating all services..."
for %%f in (%~dp0/services/*.yaml) do (
    echo "Deploying %%~nxf"
    kubectl apply -f "%~dp0/services/%%~nxf" -n "%namespace%"
)

echo "All services started... Printing K8s output.."
kubectl get all -n "%namespace%"
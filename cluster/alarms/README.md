# AlarmsIOTSimulator
A .NET Core console app to publish simple alarm data to an Azure Event Grid topic. 

The alarm data consists of:

- device id
- longtitude
- latitude
- image (a URL to an image related to the alarm)
- name 
- text

Resultant JSON Schema:

```JSON
{
    "properties": {
        "DeviceId": {
            "type": "number"
        },
        "Image": {
            "type": "string"
        },
        "Latitude": {
            "type": "number"
        },
        "Longitude": {
            "type": "number"
        },
        "Name": {
            "type": "string"
        },
        "Text": {
            "type": "string"
        }
    },
    "type": "object"
}
```

## Pre-reqs

You will need an [Azure Event Grid topic](https://docs.microsoft.com/en-us/azure/event-grid/custom-event-quickstart-portal#create-a-custom-topic).

## Usage

The following environment variables are required to be set before running from the command line:

- AlarmTopic - The Event Grid Topic EndPoint.
- AlarmKey - The Event Grid Topic key.
- AlarmImageRoot - The URL to the source of the alarm images. Each image in the folder must be named photoXX.png where XX = 01, 02, 03 etc..

The following environment variables are optional:

- AlarmImageNumber - The number of images in the image URL. Minimum of 2, default = 20.
- AlarmInterval - The ms between alarm events, default = 30000.
- AlarmNumDevices - The number of alarms, default = 10.
- AlarmMaxLat AlarmMinLat AlarmMaxLong AlarmMinLong - Describes the area within which random cordinates will be created, default = central England. Latitude and Longitude must all be decimal with 6 significant points and all 4 must be provided.
- AlarmStatusWeight - Must be more than 2, the lower the weighting the proportionally more true alarm images. Default = 10.
- AlarmMaxRunTime - The maximum number of minutes for the events to be generated, zero for no max. The simulator will stop after this time. Default = 60.

Then from the command line run:

`dotnet run`
            
You can also build a Docker image using the included Dockerfile such as: 

`docker build --rm -f Dockerfile -t alarmsiotsimulator:latest .`

The [image is available on DockerHub](https://hub.docker.com/r/gdavi/alarms-iot-simulator/) to use immediately. To pass the environment variables into the docker container you can use the following:

`docker run -e AlarmTopic="[TOPIC URL]" -e AlarmKey="[TOPIC KEY]" -e AlarmImageRoot="[IMAGES ROOT URL]" gdavi/alarms-iot-simulator`

To run in Azure Container Instance via the Azure CLI or command shell:

`az container create --resource-group [RESOURCE GROUP] --name [NAME] --image gdavi/alarms-iot-simulator --restart-policy Never --environment-variables AlarmTopic=[TOPIC URL] AlarmKey=[TOPIC KEY] AlarmImageRoot=[IMAGES ROOT URL]`

To stop and delete in Azure Container Instance via the Azure CLI or command shell:

`az container delete --name [NAME] -g [RESOURCE GROUP]`
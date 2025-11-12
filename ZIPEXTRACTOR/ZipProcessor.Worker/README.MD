1.zipprocessor.worker:

	ZipProcessor.Worker is a .NET 8 background processing service with an integrated HTTP control API.
    It continuously processes pending records from a MySQL database, invokes external APIs, executes stored procedures, and logs results.
	
	1.a Configuration :

		Program.cs - Defines all HTTP endpoints and config setup.

		WorkerService.cs - Implements the continuous background processing logic.

		appsettings.json - Defines connection strings and worker configuration.

		Docker ready with environment overrides.

	
	1.b Configurations are loaded from:

		appsettings.json

		Environment variables

		Command line arguments

		In-memory (for runtime updates via API)

	1.c Control API Endpoints : 

		Endpoint	Method	Description
		/control/status	GET	Get worker’s current state (Active / Paused / Stopped)
		/control/pause	POST	Pause worker
		/control/resume	POST	Resume worker
		/control/stop	POST	Stop worker
		/control/start	POST	Restart worker after stop
		/control/update	POST	Update runtime configuration (in-memory)
		/control/current	GET	Get current effective configuration
		/health	GET	Health check endpoint


	1.d The background worker:

		Connects to the database.

		Selects pending records (request_jobs table).

		Sends an API request using ApiSettings:BaseUrl.

		Invokes stored procedure InsertDataFromApiResp with API result.

		Logs results to api_results table.

		Updates workers & health tables for monitoring and heartbeat.

		Dynamic Configuration (via API)

	1.e Example PowerShell command to update worker settings:

		Invoke-WebRequest `
		  -Uri "http://localhost:8080/control/update" 
		  -Method POST 
		  -Headers @{ "Content-Type" = "application/json" } 
		  -Body '{"ApiBaseUrl":"http://localhost:6000/api/process","BatchSize":10,"MaxParallel":4,"DelayMilliseconds":5000}'


		- Immediately updates the values in memory — no restart required.
		- Logs changes in console + automatically applies to the next cycle.



	1.f Docker Setup :

		 Build Image
		docker build -t myzipworker:latest .

		 Run Container
		docker run -d --name zipworker -p 8080:8080 \
		  -e ConnectionStrings__DefaultConnection="Server=host.docker.internal;Database=zipdb;User ID=zipuser;Password=zippass;" \
		  -e ConnectionStrings__ProcedureDbConnection="Server=host.docker.internal;Database=zipdb_test;User=ziptestuser;Password=ziptestpass;" \
		  -e ApiSettings__BaseUrl=http://host.docker.internal:5000/api/process \
		  -e ApiSettings__WorkerUrl=http://0.0.0.0:8080 \
		  -e WorkerSettings__BatchSize=5 \
		  -e WorkerSettings__MaxParallel=3 \
		  -e WorkerSettings__DelayMilliseconds=3000 \
		  myzipworker:latest

		 Verify Environment Variables
		docker exec -it zipworker /bin/bash
		printenv | grep ConnectionStrings

	1.g Database Tables Used :

		Table	Purpose
		request_jobs	Queue of files to process
		api_results	Stores results of API + SP
		workers	Tracks worker instance status
		health	Stores heartbeat & uptime
		zipdb_test	Used by stored procedure InsertDataFromApiResp

	1.h Worker Flow Summary :

		Fetch pending records (Pending status).

		Mark them as Processing.

		Call external API → log status code & response.

		Call MySQL stored procedure with API response JSON.

		Mark record as Completed or Error.

		Update heartbeat every 15s.

		Support live pause/resume/stop.

	1.i Common Developer Commands :

		Command	Purpose
		dotnet build	Build the project
		dotnet run	Run locally
		curl http://localhost:8080/control/status	Check worker status
		curl -X POST http://localhost:8080/control/pause	Pause worker
		curl -X POST http://localhost:8080/control/resume	Resume worker
		curl http://localhost:8080/control/current	View active config
		curl http://localhost:8080/health	Check health
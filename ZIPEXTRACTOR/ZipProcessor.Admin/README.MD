2.zipprocessor.admin

	ZipProcessor.Admin is a .NET 8 MVC-based admin dashboard for managing multiple background worker instances (both Docker and executable).
	It provides a full graphical interface for starting, stopping, pausing, resuming, and monitoring worker processes, as well as calling control APIs in real time.

	It connects to a shared MySQL database (zipdb) to manage worker metadata, health, and configuration.


	It acts as the control center for all your running worker instances, integrating with:

	The ZipProcessor.Worker (background processing worker API)

	The MySQL database (for worker and service definitions)

	Docker or local executables (for actual worker runtime)

	2.a steps :

		Start Worker

		Choose a service template (e.g., “Zip Analyzer”).

		Select mode: Exe or Docker.

		Auto-assign available port if needed.

		Launch via:

		Docker: creates container with mapped port.

		Executable: runs process locally.

		Pause / Resume / Stop

		Uses /control/pause, /control/resume, /control/stop APIs from worker.

		UI dynamically updates without refresh.

		ProbeAll

		Periodically checks /control/status of each active worker.

		Updates worker status in DB and UI every 10 seconds.

		Detects stale workers (no heartbeat > 60 seconds).

		CallWorkerApi

		Allows sending arbitrary API calls to any worker.

		Displays JSON request & response in a modal popup.

		Supports request editing, beautify/minify JSON, and copy-to-clipboard.


		1.c Dashboard View (Dashboard/Index.cshtml)

			Displays all active workers with:

			Worker name, port, mode, API URL, and status.

			Real-time update (pause/resume/stop).

			Dropdown list of worker control APIs.

			“Start New Worker” modal lets user select service, type, and dynamic inputs.

		1.d Docker Dashboard (Dashboard/IndexDocker.cshtml)

			Specialized real-time dashboard for Docker-based workers.

			Displays:

			Worker ID, container name, status

			Last heartbeat

			Time difference (created → heartbeat)

			API URL & performance metrics

			Buttons to invoke worker APIs directly.

		1.e Database Integration

			Table	Purpose
			service_master	Stores available worker templates (from Services section)
			worker_registry	Tracks started worker instances
			api_master	Lists API endpoints available for call
			workers, health	Populated by each worker instance

		1.f Docker Deployment

			 Create Docker Network
			docker network create zipnet

			🏗 Build Image
			docker build -t zipprocessor.admin:latest .

			▶️ Run Container
			docker run -d --name zipgui --network zipnet -p 5183:8080 \
			  -e ASPNETCORE_ENVIRONMENT=Production \
			  -e ConnectionStrings__DefaultConnection="Server=host.docker.internal;Database=zipdb;User=zipuser;Password=zippass;" \
			  -e ApiSettings__WorkerApiBase=http://zipworker:8080 \
			  zipprocessor.admin:latest

		1.g Key Endpoints (DashboardController)


			Route	Method	Description
			/Dashboard	GET	Main worker dashboard
			/Dashboard/IndexDocker	GET	Docker worker dashboard
			/Dashboard/ProbeAll	GET	Check all worker statuses
			/Dashboard/CallWorkerApi	POST	Execute API request to worker
			/Dashboard/StartWorker	POST	Start worker process/container
			/Dashboard/PauseWorker	POST	Pause worker
			/Dashboard/ResumeWorker	POST	Resume worker
			/Dashboard/StopWorker	POST	Stop worker
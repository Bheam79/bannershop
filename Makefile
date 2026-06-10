# ─────────────────────────────────────────────────────────────────────────────
# BannerShop — production Makefile
#
# Goal: a single `make up` on a Linux host with `docker`, `dotnet` and `npm`
# in PATH stands the whole stack up — no manual config required. Secrets
# (DB password, JWT key, admin password) are generated on first run and
# persisted under $HOME/.local/share/bannershop/secrets/.
#
# Quick start:
#   make up         # generate secrets, start MariaDB, build, install & start
#   make down       # stop the systemd user service AND the MariaDB container
#   make restart    # restart only the service
#   make status     # show service + container status
#   make logs       # tail journald logs (Ctrl-C to exit)
#   make build      # rebuild backend + frontend without touching the service
#   make uninstall  # disable & remove the systemd unit (leaves data/secrets)
#
# Layout (everything under $HOME, no sudo required):
#   $HOME/.local/share/bannershop/app/       — published .NET app + wwwroot SPA
#   $HOME/.local/share/bannershop/data/      — uploads, etc.
#   $HOME/.local/share/bannershop/secrets/   — generated passwords (mode 0600)
#   $HOME/.config/systemd/user/bannershop.service
#
# Ports (from the requested 17000–17100 range):
#   17006  MariaDB (host:127.0.0.1 → container :3306)
#   17080  Backend HTTP (serves /api/* AND the SPA fallback)
#
# Notes:
#   - MariaDB runs as a Docker container; the backend and frontend do NOT.
#   - The frontend is built with Vite then copied into the backend's wwwroot,
#     so a single ASP.NET Core process serves both the API and the SPA.
#   - The service is enabled (`systemctl --user enable`) so re-running
#     `make up` after a code change does a `systemctl --user restart`
#     and the new build is picked up.
#   - The service starts only after you log in unless you also run
#     `loginctl enable-linger $USER` once as root.
# ─────────────────────────────────────────────────────────────────────────────

SERVICE_NAME  := bannershop
DB_CONTAINER  := bannershop-db
DB_NAME       := bannershop
DB_USER       := bannershop
DB_HOST_BIND  := 127.0.0.1
DB_HOST_PORT  := 17006
BACKEND_PORT  := 17080
ASPNET_URLS   := http://0.0.0.0:$(BACKEND_PORT)
ADMIN_EMAIL   := admin@bannershop.no

# ── Optional transactional-email (SMTP) ──────────────────────────────────────
# Override on the command line, e.g.
#   make up SMTP_HOST=smtp.eu.mailgun.org SMTP_USER=postmaster@... SMTP_PASS=...
# Leaving these empty wires up NullEmailService — the API still runs fine,
# but order-confirmation / shipment-dispatched mails are suppressed.
EMAIL_FROM    ?= noreply@bannershop.no
SMTP_HOST     ?=
SMTP_PORT     ?= 587
SMTP_USER     ?=
SMTP_PASS     ?=

# ── Optional AI image generation (OpenAI) ────────────────────────────────────
# Override on the command line, e.g.
#   make up OPENAI_API_KEY=sk-proj-...
# Without a real key the AI generator falls back to MockAiImageService (solid-
# colour PNG placeholder) — the rest of the pipeline still runs fine.
OPENAI_API_KEY       ?=
OPENAI_IMAGE_MODEL   ?= gpt-image-2
OPENAI_IMAGE_QUALITY ?= high

# ── Optional Replicate (Real-ESRGAN 4× upscaler, admin only) ─────────────────
# Override on the command line, e.g.
#   make up REPLICATE_API_TOKEN=r8_...
# Without a token the upscaler is disabled and admin upscale requests return 501.
REPLICATE_API_TOKEN  ?=

ROOT_DIR      := $(abspath $(dir $(lastword $(MAKEFILE_LIST))))
PROD_BASE     := $(HOME)/.local/share/bannershop
APP_DIR       := $(PROD_BASE)/app
DATA_DIR      := $(PROD_BASE)/data
UPLOADS_DIR   := $(DATA_DIR)/uploads
SECRETS_DIR   := $(PROD_BASE)/secrets
UNIT_DIR      := $(HOME)/.config/systemd/user
UNIT_FILE     := $(UNIT_DIR)/$(SERVICE_NAME).service

DOTNET        := $(shell command -v dotnet 2>/dev/null)
NPM           := $(shell command -v npm 2>/dev/null)
DOCKER        := $(shell command -v docker 2>/dev/null)

# ─────────────────────────────────────────────────────────────────────────────
.PHONY: help up down restart status logs build publish frontend config secrets \
        db-up db-down install-service stop-service start-service uninstall \
        check-tools print-admin-password \
        test test-coverage e2e-coverage

help:
	@echo "BannerShop — production Makefile"
	@echo ""
	@echo "Targets:"
	@echo "  up          Generate secrets, start MariaDB, build, install & (re)start service"
	@echo "  down        Stop systemd user service AND the MariaDB container"
	@echo "  restart     Restart only the systemd user service"
	@echo "  status      Show service and container status"
	@echo "  logs        Tail service logs (Ctrl-C to exit)"
	@echo "  build       Rebuild frontend + backend, write Production config"
	@echo "  uninstall   Disable & remove the systemd unit (keeps data/secrets/db)"
	@echo "  print-admin-password   Show the generated admin password"
	@echo ""
	@echo "Listening (after 'make up'):"
	@echo "  http://localhost:$(BACKEND_PORT)         (SPA + API)"
	@echo "  $(DB_HOST_BIND):$(DB_HOST_PORT)            (MariaDB)"
	@echo ""
	@echo "Layout under $(PROD_BASE)"

# ── Top-level orchestration ──────────────────────────────────────────────────
up: check-tools secrets db-up build install-service
	@echo ""
	@echo "==================================================================="
	@echo "  BannerShop is up at  http://localhost:$(BACKEND_PORT)"
	@echo "  Admin login:         $(ADMIN_EMAIL)"
	@echo "  Admin password:      $$(cat $(SECRETS_DIR)/admin_password)"
	@echo "==================================================================="

down: stop-service db-down
	@echo "BannerShop stopped (data + secrets preserved under $(PROD_BASE))."

restart:
	systemctl --user restart $(SERVICE_NAME)

status:
	@echo "── systemd user service ──────────────────────────────────────────"
	-@systemctl --user status $(SERVICE_NAME) --no-pager 2>/dev/null || echo "(not installed)"
	@echo ""
	@echo "── MariaDB container ─────────────────────────────────────────────"
	-@$(DOCKER) ps --filter "name=^$(DB_CONTAINER)$$" --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}" 2>/dev/null

logs:
	journalctl --user -u $(SERVICE_NAME) -f --no-pager

print-admin-password:
	@[ -f $(SECRETS_DIR)/admin_password ] && cat $(SECRETS_DIR)/admin_password \
		|| { echo "No admin password generated yet — run 'make up' first" >&2; exit 1; }

# ── Tool sanity check ────────────────────────────────────────────────────────
check-tools:
	@if [ -z "$(DOTNET)" ]; then echo "ERROR: 'dotnet' not found in PATH" >&2; exit 1; fi
	@if [ -z "$(NPM)" ];    then echo "ERROR: 'npm' not found in PATH"    >&2; exit 1; fi
	@if [ -z "$(DOCKER)" ]; then echo "ERROR: 'docker' not found in PATH" >&2; exit 1; fi
	@command -v systemctl >/dev/null || { echo "ERROR: 'systemctl' not found in PATH" >&2; exit 1; }
	@command -v openssl   >/dev/null || { echo "ERROR: 'openssl' not found in PATH"   >&2; exit 1; }

# ── Secrets ──────────────────────────────────────────────────────────────────
# Generates strong random secrets on first run. Idempotent: existing files
# are kept so re-running `make up` doesn't rotate keys.
secrets:
	@mkdir -p $(SECRETS_DIR)
	@chmod 700 $(SECRETS_DIR)
	@if [ ! -f $(SECRETS_DIR)/db_root_password ]; then \
		( umask 077 && openssl rand -hex 24 > $(SECRETS_DIR)/db_root_password ); \
	fi
	@if [ ! -f $(SECRETS_DIR)/db_password ]; then \
		( umask 077 && openssl rand -hex 24 > $(SECRETS_DIR)/db_password ); \
	fi
	@if [ ! -f $(SECRETS_DIR)/jwt_secret ]; then \
		( umask 077 && openssl rand -base64 48 | tr -d '\n=+/' | head -c 64 > $(SECRETS_DIR)/jwt_secret ); \
	fi
	@if [ ! -f $(SECRETS_DIR)/admin_password ]; then \
		( umask 077 && openssl rand -hex 16 > $(SECRETS_DIR)/admin_password ); \
		echo ""; \
		echo ">>> Generated admin password (saved to $(SECRETS_DIR)/admin_password):"; \
		echo ">>>     $(ADMIN_EMAIL)  /  $$(cat $(SECRETS_DIR)/admin_password)"; \
		echo ""; \
	fi

# ── Database ─────────────────────────────────────────────────────────────────
db-up: secrets
	@mkdir -p $(DATA_DIR)
	@if [ -n "$$($(DOCKER) ps -q -f name=^$(DB_CONTAINER)$$)" ]; then \
		echo "MariaDB container '$(DB_CONTAINER)' is already running."; \
	elif [ -n "$$($(DOCKER) ps -aq -f name=^$(DB_CONTAINER)$$)" ]; then \
		echo "Starting existing MariaDB container '$(DB_CONTAINER)'..."; \
		$(DOCKER) start $(DB_CONTAINER) >/dev/null; \
	else \
		echo "Creating MariaDB container '$(DB_CONTAINER)' on $(DB_HOST_BIND):$(DB_HOST_PORT)..."; \
		$(DOCKER) run -d \
			--name $(DB_CONTAINER) \
			--restart unless-stopped \
			-p $(DB_HOST_BIND):$(DB_HOST_PORT):3306 \
			-e MYSQL_ROOT_PASSWORD="$$(cat $(SECRETS_DIR)/db_root_password)" \
			-e MYSQL_DATABASE=$(DB_NAME) \
			-e MYSQL_USER=$(DB_USER) \
			-e MYSQL_PASSWORD="$$(cat $(SECRETS_DIR)/db_password)" \
			-v bannershop_dbdata:/var/lib/mysql \
			docker.io/library/mariadb:11 >/dev/null; \
	fi
	@echo "Waiting for MariaDB to accept connections..."
	@for i in $$(seq 1 60); do \
		if $(DOCKER) exec $(DB_CONTAINER) mariadb-admin ping -uroot -p"$$(cat $(SECRETS_DIR)/db_root_password)" --silent >/dev/null 2>&1; then \
			echo "MariaDB ready."; exit 0; \
		fi; \
		sleep 1; \
	done; \
	echo "ERROR: MariaDB did not become ready in 60s" >&2; \
	$(DOCKER) logs --tail 50 $(DB_CONTAINER) >&2 || true; \
	exit 1

db-down:
	-@$(DOCKER) stop $(DB_CONTAINER) >/dev/null 2>&1 || true
	@echo "MariaDB container stopped (data volume bannershop_dbdata kept)."

# ── Frontend → wwwroot ───────────────────────────────────────────────────────
frontend:
	@echo ">>> Building frontend (Vite)..."
	cd $(ROOT_DIR)/frontend && $(NPM) install --no-audit --no-fund
	cd $(ROOT_DIR)/frontend && $(NPM) run build
	@echo ">>> Copying frontend/dist → BannerShop.Api/wwwroot"
	rm -rf $(ROOT_DIR)/BannerShop.Api/wwwroot
	mkdir -p $(ROOT_DIR)/BannerShop.Api/wwwroot
	cp -a $(ROOT_DIR)/frontend/dist/. $(ROOT_DIR)/BannerShop.Api/wwwroot/

# ── Backend publish ──────────────────────────────────────────────────────────
publish: frontend
	@echo ">>> Publishing backend (.NET Release) → $(APP_DIR)"
	@mkdir -p $(APP_DIR) $(UPLOADS_DIR)
	$(DOTNET) publish $(ROOT_DIR)/BannerShop.Api/BannerShop.Api.csproj \
		-c Release \
		-o $(APP_DIR) \
		-p:UseAppHost=false \
		--nologo

# ── Production appsettings ───────────────────────────────────────────────────
# Written next to BannerShop.Api.dll. ASP.NET picks it up because the service
# unit sets ASPNETCORE_ENVIRONMENT=Production.
config: secrets publish
	@echo ">>> Writing $(APP_DIR)/appsettings.Production.json"
	@JWT_SECRET=$$(cat $(SECRETS_DIR)/jwt_secret); \
	 DB_PASSWORD=$$(cat $(SECRETS_DIR)/db_password); \
	 ADMIN_PASSWORD=$$(cat $(SECRETS_DIR)/admin_password); \
	 umask 077; \
	 printf '%s\n' \
	'{' \
	'  "Logging": {' \
	'    "LogLevel": {' \
	'      "Default": "Information",' \
	'      "Microsoft.AspNetCore": "Warning",' \
	'      "Microsoft.EntityFrameworkCore": "Warning"' \
	'    }' \
	'  },' \
	'  "AllowedHosts": "*",' \
	'  "ConnectionStrings": {' \
	'    "DefaultConnection": "Server=$(DB_HOST_BIND);Port=$(DB_HOST_PORT);Database=$(DB_NAME);User=$(DB_USER);Password='"$$DB_PASSWORD"';"' \
	'  },' \
	'  "Jwt": {' \
	'    "Secret": "'"$$JWT_SECRET"'",' \
	'    "Issuer": "bannershop.no",' \
	'    "Audience": "bannershop.no",' \
	'    "AccessTokenExpiryMinutes": 60,' \
	'    "RefreshTokenExpiryDays": 30' \
	'  },' \
	'  "Admin": {' \
	'    "SeedEmail": "$(ADMIN_EMAIL)",' \
	'    "SeedPassword": "'"$$ADMIN_PASSWORD"'"' \
	'  },' \
	'  "Frontend": {' \
	'    "Url": "http://localhost:$(BACKEND_PORT)"' \
	'  },' \
	'  "FileStorage": {' \
	'    "Provider": "LocalDisk",' \
	'    "LocalRoot": "$(UPLOADS_DIR)",' \
	'    "PublicBaseUrl": "/files",' \
	'    "MaxUploadBytes": 52428800,' \
	'    "AllowedMimeTypes": [ "image/jpeg", "image/png", "image/webp", "application/pdf" ]' \
	'  },' \
	'  "Email": {' \
	'    "From": "$(EMAIL_FROM)",' \
	'    "SmtpHost": "$(SMTP_HOST)",' \
	'    "SmtpPort": $(SMTP_PORT),' \
	'    "SmtpUser": "$(SMTP_USER)",' \
	'    "SmtpPass": "$(SMTP_PASS)"' \
	'  },' \
	'  "OpenAi": {' \
	'    "ApiKey": "$(OPENAI_API_KEY)",' \
	'    "ImageModel": "$(OPENAI_IMAGE_MODEL)",' \
	'    "ImageQuality": "$(OPENAI_IMAGE_QUALITY)",' \
	'    "OrgId": "",' \
	'    "BaseUrl": "https://api.openai.com",' \
	'    "TimeoutSeconds": 180' \
	'  },' \
	'  "Replicate": {' \
	'    "ApiToken": "$(REPLICATE_API_TOKEN)",' \
	'    "RealEsrganModelVersion": "f121d640bd286e1fdc67f9799164c1d5be36ff74576ee11c803ae5b665dd46aa",' \
	'    "BaseUrl": "https://api.replicate.com",' \
	'    "TimeoutSeconds": 60,' \
	'    "PollIntervalMs": 2000,' \
	'    "MaxPollSeconds": 600' \
	'  }' \
	'}' > $(APP_DIR)/appsettings.Production.json
	@if [ -f $(ROOT_DIR)/BannerShop.Api/appsettings.Local.json ]; then \
		echo ">>> Validating appsettings.Local.json (JSON parse check)..."; \
		node -e "JSON.parse(require('fs').readFileSync('$(ROOT_DIR)/BannerShop.Api/appsettings.Local.json','utf8'))" \
		  || { echo "" >&2; echo "ERROR: BannerShop.Api/appsettings.Local.json is not valid JSON." >&2; \
		       echo "       Fix the syntax error shown above and re-run 'make up'." >&2; exit 1; }; \
		echo ">>> Copying appsettings.Local.json → $(APP_DIR)/"; \
		umask 077 && cp $(ROOT_DIR)/BannerShop.Api/appsettings.Local.json $(APP_DIR)/appsettings.Local.json; \
	elif [ -f $(APP_DIR)/appsettings.Local.json ]; then \
		echo ">>> No source-side appsettings.Local.json — validating deployed copy at $(APP_DIR)/..."; \
		node -e "JSON.parse(require('fs').readFileSync('$(APP_DIR)/appsettings.Local.json','utf8'))" \
		  || { echo "" >&2; echo "ERROR: Deployed appsettings.Local.json at $(APP_DIR)/ is not valid JSON." >&2; \
		       echo "       Fix the syntax error shown above and re-run 'make up'." >&2; exit 1; }; \
		echo ">>> Keeping existing deployed copy at $(APP_DIR)/appsettings.Local.json"; \
	else \
		echo ">>> No appsettings.Local.json in source tree or deployed dir — skipping."; \
	fi

build: config

# ── systemd user service ─────────────────────────────────────────────────────
install-service: build
	@mkdir -p $(UNIT_DIR)
	@echo ">>> Writing $(UNIT_FILE)"
	@printf '%s\n' \
	'[Unit]' \
	'Description=BannerShop backend (.NET, serves API + SPA)' \
	'After=network-online.target' \
	'Wants=network-online.target' \
	'' \
	'[Service]' \
	'Type=simple' \
	'WorkingDirectory=$(APP_DIR)' \
	'Environment=ASPNETCORE_ENVIRONMENT=Production' \
	'Environment=ASPNETCORE_URLS=$(ASPNET_URLS)' \
	'Environment=DOTNET_NOLOGO=1' \
	'ExecStart=$(DOTNET) $(APP_DIR)/BannerShop.Api.dll' \
	'Restart=always' \
	'RestartSec=3' \
	'' \
	'[Install]' \
	'WantedBy=default.target' \
	> $(UNIT_FILE)
	systemctl --user daemon-reload
	systemctl --user enable $(SERVICE_NAME)
	systemctl --user restart $(SERVICE_NAME)
	@sleep 2
	@if ! systemctl --user is-active --quiet $(SERVICE_NAME); then \
		echo "ERROR: service '$(SERVICE_NAME)' is not active. Recent log:" >&2; \
		journalctl --user -u $(SERVICE_NAME) -n 60 --no-pager >&2; \
		exit 1; \
	fi

# ── Test & coverage ──────────────────────────────────────────────────────────
# Run unit tests (no coverage)
test:
	dotnet test $(ROOT_DIR)/BannerShop.slnx --nologo -v quiet

# Run unit tests and generate an HTML coverage report → ./coverage-report/
# Requires the dotnet-reportgenerator-globaltool local tool (see dotnet-tools.json).
test-coverage:
	@bash $(ROOT_DIR)/scripts/test-coverage.sh

# Run Playwright e2e tests with Istanbul frontend coverage → e2e/coverage/
# Requires: API backend running on localhost:5000 (dotnet run or make up).
e2e-coverage:
	@bash $(ROOT_DIR)/scripts/e2e-coverage.sh

stop-service:
	-@systemctl --user stop $(SERVICE_NAME) 2>/dev/null || true

start-service:
	systemctl --user start $(SERVICE_NAME)

uninstall:
	-@systemctl --user disable --now $(SERVICE_NAME) 2>/dev/null || true
	-@rm -f $(UNIT_FILE)
	-@systemctl --user daemon-reload 2>/dev/null || true
	@echo "Service uninstalled. Data, secrets and the MariaDB container/volume are untouched."

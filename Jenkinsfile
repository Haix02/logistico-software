// Logistico's Jenkins pipeline (docs/11 §7). The same checks as .github/workflows/ci.yml, plus the QA suite: the real
// server, role agents working a whole delivery day, and a real browser on every page. Needs an agent labelled "dotnet"
// with the .NET SDK, Flutter, Python 3, Chrome and a PostgreSQL server (credential logistico-test-db).
pipeline {
    agent { label 'dotnet' }

    parameters {
        booleanParam(name: 'PACKAGE', defaultValue: false, description: 'Also build the release package on this branch (always done on main)')
    }

    options {
        timestamps()
        timeout(time: 60, unit: 'MINUTES')
        disableConcurrentBuilds(abortPrevious: true)
        buildDiscarder(logRotator(numToKeepStr: '30', artifactNumToKeepStr: '10'))
    }

    environment {
        DOTNET_NOLOGO = 'true'
        DOTNET_CLI_TELEMETRY_OPTOUT = 'true'
        LOGISTICO_TEST_DB = credentials('logistico-test-db')
    }

    stages {
        stage('Tools') {
            steps {
                sh '''
                    dotnet --version
                    python3 --version
                    flutter --version | head -n 1
                    pg_isready || { echo "PostgreSQL is not answering on this agent"; exit 1; }
                '''
            }
        }

        // NuGet audit runs during restore: known-vulnerable packages fail the build (warnings are errors).
        stage('Restore') {
            steps {
                sh '''
                    dotnet restore Logistico.slnx
                    dotnet tool restore
                '''
            }
        }

        stage('Format') {
            steps {
                sh 'dotnet format Logistico.slnx --verify-no-changes --severity warn --no-restore'
            }
        }

        stage('Build') {
            steps {
                sh 'dotnet build Logistico.slnx -c Release --no-restore'
            }
        }

        stage('Checks') {
            parallel {
                stage('Tests and coverage') {
                    steps {
                        sh '''
                            dotnet test Logistico.slnx -c Release --no-build --filter "Category!=Qa" \
                              --logger "junit;LogFilePath=$WORKSPACE/TestResults/junit/{assembly}.xml;MethodFormat=Class;FailureBodyFormat=Verbose" \
                              --results-directory TestResults --collect:"XPlat Code Coverage"
                            python3 scripts/coverage-summary.py TestResults --min 85
                        '''
                    }
                }

                stage('Migrations match the model') {
                    environment {
                        ConnectionStrings__Logistico = "${env.LOGISTICO_TEST_DB}"
                    }
                    steps {
                        sh '''
                            for ctx in "src/Logistico.Web:ApplicationDbContext" "src/Modules/Shipments/Logistico.Modules.Shipments:ShipmentsDbContext" \
                                       "src/Modules/Merchants/Logistico.Modules.Merchants:MerchantsDbContext" "src/Modules/Audit/Logistico.Modules.Audit:AuditDbContext"; do
                              dotnet ef migrations has-pending-model-changes --no-build --configuration Release \
                                --project "${ctx%%:*}" --startup-project src/Logistico.Web --context "${ctx##*:}"
                            done
                        '''
                    }
                }

                stage('Mobile app') {
                    steps {
                        dir('mobile') {
                            sh '''
                                flutter pub get
                                flutter gen-l10n
                                python3 tool/generate_domain_texts.py --check
                                flutter analyze
                                flutter test --reporter expanded
                            '''
                        }
                    }
                }
            }
        }

        // The real server on a real port with its own throw-away database (tests/Logistico.Qa).
        stage('QA: agents and browser') {
            steps {
                sh '''
                    dotnet test tests/Logistico.Qa -c Release --no-build \
                      --logger "junit;LogFilePath=$WORKSPACE/TestResults/junit/{assembly}.xml;MethodFormat=Class;FailureBodyFormat=Verbose" \
                      --logger "console;verbosity=normal"
                '''
            }
        }

        stage('Docker image') {
            when {
                expression { sh(returnStatus: true, script: 'command -v docker > /dev/null') == 0 }
            }
            steps {
                sh 'docker build --tag "logistico-web:jenkins-$BUILD_NUMBER" .'
            }
        }

        stage('Package') {
            when {
                anyOf {
                    branch 'main'
                    expression { params.PACKAGE }
                }
            }
            steps {
                sh '''
                    dotnet publish src/Logistico.Web -c Release --no-build -o out/logistico-web
                    tar -czf "out/logistico-web-$BUILD_NUMBER.tar.gz" -C out logistico-web
                    # Production applies migrations from these bundles before the new version starts (docs/11).
                    dotnet ef migrations bundle --project src/Modules/Audit/Logistico.Modules.Audit --startup-project src/Logistico.Web \
                      --context AuditDbContext -o out/migrate-audit --self-contained -r linux-x64
                    dotnet ef migrations bundle --project src/Logistico.Web --startup-project src/Logistico.Web \
                      --context ApplicationDbContext -o out/migrate-identity --self-contained -r linux-x64
                    dotnet ef migrations bundle --project src/Modules/Shipments/Logistico.Modules.Shipments --startup-project src/Logistico.Web \
                      --context ShipmentsDbContext -o out/migrate-shipments --self-contained -r linux-x64
                    dotnet ef migrations bundle --project src/Modules/Merchants/Logistico.Modules.Merchants --startup-project src/Logistico.Web \
                      --context MerchantsDbContext -o out/migrate-merchants --self-contained -r linux-x64
                '''
                archiveArtifacts artifacts: 'out/*.tar.gz,out/migrate-*', fingerprint: true
            }
        }
    }

    post {
        always {
            junit testResults: 'TestResults/junit/*.xml', allowEmptyResults: true
            recordCoverage tools: [[parser: 'COBERTURA', pattern: 'TestResults/**/coverage.cobertura.xml']], sourceCodeRetention: 'NEVER'
            archiveArtifacts artifacts: 'TestResults/junit/*.xml,TestResults/qa/**', allowEmptyArchive: true
        }
        cleanup {
            cleanWs notFailBuild: true
        }
    }
}

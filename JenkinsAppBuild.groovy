import groovy.json.JsonOutput
import groovy.transform.Field

@Field def CI_PATHS = [
    macProjectRoot          : '<MAC_PROJECT_ROOT>',
    windowsProjectRoot      : '<WINDOWS_PROJECT_ROOT>',
    oneStoreProjectRoot     : '<ONESTORE_PROJECT_ROOT>',
    androidProjectRoot      : '<ANDROID_PROJECT_ROOT>',
    windowsOutputRoot       : '<WINDOWS_OUTPUT_ROOT>',
    windowsSymbolsRoot      : '<WINDOWS_SYMBOLS_ROOT>',
    iosOutputRoot           : '<IOS_OUTPUT_ROOT>',
    windowsGitExecutable    : '<WINDOWS_GIT_EXECUTABLE>',
    macUnityExecutable      : '<MAC_UNITY_EXECUTABLE>',
    windowsUnityExecutable  : '<WINDOWS_UNITY_EXECUTABLE>',
    gradleUserHomeRoot      : '<GRADLE_USER_HOME_ROOT>',
    macPodExecutable        : '<COCOAPODS_EXECUTABLE>',
    macPodFallbackExecutable: '<COCOAPODS_FALLBACK_EXECUTABLE>',
    macAwsExecutable        : '<MAC_AWS_CLI_EXECUTABLE>'
]

@Field def CI_STORAGE = [
    s3Uri       : '<S3_BUCKET_URI>',
    bucketName  : '<S3_BUCKET_NAME>',
    awsRegion   : '<AWS_REGION>'
]

@Field def CI_CREDENTIALS = [
    slackWebhook : '<SLACK_WEBHOOK_CREDENTIAL_ID>',
    ascApiKeyFile: '<APPLE_API_KEY_FILE_CREDENTIAL_ID>',
    ascApiKeyId  : '<APPLE_API_KEY_ID_CREDENTIAL_ID>',
    ascIssuerId  : '<APPLE_ISSUER_ID_CREDENTIAL_ID>'
]

@Field def APPLE_TEAM_ID = '<APPLE_TEAM_ID>'
@Field def S3_Bucket = CI_STORAGE.s3Uri

def slackNotify(String text, Map opts = [:]) {
    boolean enabled =
            opts.containsKey('enabled') ? (opts.enabled as boolean)
                    : (params.SLACK_ENABLED?.toString()?.toBoolean() ?: false)

    if (!enabled) {
        echo "[Slack disabled] ${text}"
        return
    }

    def payload = JsonOutput.toJson([text: text])

    withCredentials([string(credentialsId: CI_CREDENTIALS.slackWebhook, variable: 'SLACK_WEBHOOK_URL')]) {
        if (isUnix()) {
            sh """
                curl -sS -X POST -H 'Content-type: application/json' \
                  --data @- "$SLACK_WEBHOOK_URL" <<'JSON'
${payload}
JSON
            """
        } else {
            powershell """
                \$payload = '${payload}'.Replace("'", "''")
                Invoke-RestMethod -Method Post -ContentType 'application/json' -Body \$payload -Uri \$env:SLACK_WEBHOOK_URL | Out-Null
            """
        }
    }
}

// S3_Bucket is configured in CI_STORAGE above.

pipeline {
    agent none

    parameters {
        string(name: 'VERSION_STRING', defaultValue: '1.0.0', description: '메이저.마이너.패치')
        string(name: 'BRANCH_NAME', defaultValue: 'main', description: '깃 브랜치 설정')
        booleanParam(name: 'AUTO_MOBILE_BUNDLE_CODE', defaultValue: false, description: 'true = S3 VersionCode 자동 증가 사용')
        string(name: 'MOBILE_BUNDLE_CODE', defaultValue: '1', description: 'Android/iOS 번들 버전 코드')
        booleanParam(name: 'IsApk', defaultValue: true, description: 'true = .apk, false = .aab')
        choice(name: 'BUILD_TARGET', choices: ['GooglePlay', 'iOS', 'Windows', 'OneStore'], description: '빌드 타겟')
        choice(name: 'IOS_DIST_MODE', choices: ['INTERNAL_IPA', 'TESTFLIGHT_INTERNAL_ONLY', 'APPSTORE_UPLOAD', 'BOTH'], description: 'iOS 배포 방식')
        choice(name: 'TARGET_SERVER', choices: ['ALPHA', 'LIVE', 'CHEAT', 'DEV', 'BETA'], description: 'Window S3 Upload Target')
        string(name: 'AWS_CRED_ID', defaultValue: '<AWS_CREDENTIAL_ID>', description: 'Jenkins AWS Credentials ID')
        booleanParam(name: 'IsAssetBundle', defaultValue: true, description: '파이프라인 성공 후 에셋번들로 바로 뽑을 것인가?')
        booleanParam(name: 'SLACK_ENABLED', defaultValue: true, description: '슬랙 알림 사용')
    }

    stages {
        stage('Build') {
            agent any

            steps {
                script {
                    slackNotify("🛠 ${params.BUILD_TARGET} ${params.TARGET_SERVER} ${params.VERSION_STRING} 빌드 시작")
                }
            }
        }

        stage('Resolve Mobile Bundle Code') {
            agent {
                label "${params.BUILD_TARGET == 'iOS' ? 'mac' : 'windows'}"
            }

            steps {
                withCredentials([[$class: 'AmazonWebServicesCredentialsBinding', credentialsId: "${params.AWS_CRED_ID}"]]) {
                    script {
                        def shouldAutoIncreaseMobileBundleCode =
                                params.AUTO_MOBILE_BUNDLE_CODE &&
                                        params.TARGET_SERVER == 'LIVE' &&
                                        (
                                                (params.BUILD_TARGET == 'GooglePlay' && params.IsApk == false) ||
                                                        (params.BUILD_TARGET == 'OneStore')
                                        )

                        if (shouldAutoIncreaseMobileBundleCode) {
                            if (params.BUILD_TARGET == 'OneStore') {
                                env.MOBILE_BUNDLE_CODE_S3_PATH =
                                        '${S3_Bucket}/ONESTORE_BUNDLE_CODE.txt'
                            } else {
                                env.MOBILE_BUNDLE_CODE_S3_PATH =
                                        '${S3_Bucket}/GOOGLE_BUNDLE_CODE.txt'
                            }

                            def currentCode = bat(
                                    script: "@aws s3 cp ${env.MOBILE_BUNDLE_CODE_S3_PATH} - --quiet",
                                    returnStdout: true
                            ).trim()

                            if (!(currentCode ==~ /^[0-9]+$/)) {
                                error "${params.BUILD_TARGET} S3 VersionCode 값이 숫자가 아닙니다: '${currentCode}'"
                            }

                            def nextCode = (currentCode as int) + 1

                            env.PREV_MOBILE_BUNDLE_CODE = currentCode
                            env.EFFECTIVE_MOBILE_BUNDLE_CODE = nextCode.toString()

                            echo """
==================================================
${params.BUILD_TARGET} VersionCode 자동 증가
S3 PATH : ${env.MOBILE_BUNDLE_CODE_S3_PATH}
현재값  : ${env.PREV_MOBILE_BUNDLE_CODE}
빌드값  : ${env.EFFECTIVE_MOBILE_BUNDLE_CODE}
==================================================
"""
                        } else {
                            if (!(params.MOBILE_BUNDLE_CODE ==~ /^[0-9]+$/)) {
                                error "MOBILE_BUNDLE_CODE 값이 숫자가 아닙니다: '${params.MOBILE_BUNDLE_CODE}'"
                            }

                            env.EFFECTIVE_MOBILE_BUNDLE_CODE =
                                    params.MOBILE_BUNDLE_CODE

                            echo "MOBILE_BUNDLE_CODE 수동 사용: ${env.EFFECTIVE_MOBILE_BUNDLE_CODE}"
                        }
                    }
                }
            }

            post {
                failure {
                    script {
                        slackNotify('❌ MOBILE_BUNDLE_CODE 설정 실패')
                    }
                }
            }
        }

        stage('Git') {
            agent {
                label "${params.BUILD_TARGET == 'iOS' ? 'mac' : 'windows'}"
            }

            steps {
                script {
                    def projectPath =
                            params.BUILD_TARGET == 'iOS' ? CI_PATHS.macProjectRoot :
                                    params.BUILD_TARGET == 'Windows' ? CI_PATHS.windowsProjectRoot :
                                            params.BUILD_TARGET == 'OneStore' ? CI_PATHS.oneStoreProjectRoot :
                                                    CI_PATHS.androidProjectRoot

                    echo "Git 최신 상태로 동기화 중... 프로젝트 경로: ${projectPath}, 브랜치: ${params.BRANCH_NAME}"

                    if (params.BUILD_TARGET == 'iOS') {
                        sh """
                            cd "${projectPath}"
                            git fetch origin "${params.BRANCH_NAME}"
                            git checkout -f -B "${params.BRANCH_NAME}" "origin/${params.BRANCH_NAME}"
                            git reset --hard "origin/${params.BRANCH_NAME}"
                        """
                    } else {
                        def gitExe = CI_PATHS.windowsGitExecutable

                        bat """
                            cd /d "${projectPath}"
                            "${gitExe}" fetch origin "${params.BRANCH_NAME}"
                            "${gitExe}" checkout -f -B "${params.BRANCH_NAME}" "origin/${params.BRANCH_NAME}"
                            "${gitExe}" reset --hard "origin/${params.BRANCH_NAME}"
                        """
                    }
                }
            }

            post {
                failure {
                    script {
                        slackNotify("❌ ${params.BUILD_TARGET} GitPull 실패")
                    }
                }
            }
        }

        stage('IAP 패키지 선택') {
            agent {
                label "${params.BUILD_TARGET == 'iOS' ? 'mac' : 'windows'}"
            }

            steps {
                script {
                    def projectPath =
                            params.BUILD_TARGET == 'iOS'
                                    ? CI_PATHS.macProjectRoot
                                    : params.BUILD_TARGET == 'GooglePlay'
                                    ? CI_PATHS.androidProjectRoot
                                    : null

                    def packageProfile =
                            params.BUILD_TARGET == 'iOS'
                                    ? 'iOS_4.13.0'
                                    : params.BUILD_TARGET == 'GooglePlay'
                                    ? 'Android_5.4.2'
                                    : null

                    if (projectPath == null || packageProfile == null) {
                        echo "${params.BUILD_TARGET}: IAP 패키지 변경 없음"
                        return
                    }

                    echo "${params.BUILD_TARGET}: ${packageProfile} 적용"

                    if (params.BUILD_TARGET == 'iOS') {
                        sh """#!/bin/bash
                    set -euxo pipefail

                    SOURCE="${projectPath}/CI/IAP/${packageProfile}"

                    test -f "\$SOURCE/manifest.json"
                    test -f "\$SOURCE/packages-lock.json"

                    cp "\$SOURCE/manifest.json" \
                       "${projectPath}/Packages/manifest.json"

                    cp "\$SOURCE/packages-lock.json" \
                       "${projectPath}/Packages/packages-lock.json"

                    grep '"com.unity.purchasing"' \
                         "${projectPath}/Packages/manifest.json"
                """
                    } else {
                        powershell """
                    \$source = '${projectPath}/CI/IAP/${packageProfile}'

                    if (!(Test-Path -LiteralPath "\$source/manifest.json")) {
                        throw "manifest.json이 없습니다: \$source"
                    }

                    if (!(Test-Path -LiteralPath "\$source/packages-lock.json")) {
                        throw "packages-lock.json이 없습니다: \$source"
                    }

                    Copy-Item -Force -LiteralPath "\$source/manifest.json" `
                        -Destination '${projectPath}/Packages/manifest.json'

                    Copy-Item -Force -LiteralPath "\$source/packages-lock.json" `
                        -Destination '${projectPath}/Packages/packages-lock.json'

                    \$manifest = Get-Content -Raw `
                        '${projectPath}/Packages/manifest.json' | ConvertFrom-Json

                    Write-Host "Unity IAP version: \$(
                        \$manifest.dependencies.'com.unity.purchasing'
                    )"
                """
                    }
                }
            }

            post {
                failure {
                    script {
                        slackNotify("❌ ${params.BUILD_TARGET} IAP 패키지 적용 실패")
                    }
                }
            }
        }

        stage('Unity 빌드') {
            agent {
                label "${params.BUILD_TARGET == 'iOS' ? 'mac' : 'windows'}"
            }

            steps {
                script {
                    try {
                        def unityPath = params.BUILD_TARGET == 'iOS'
                                ? CI_PATHS.macUnityExecutable
                                : CI_PATHS.windowsUnityExecutable

                        def projectPath =
                                params.BUILD_TARGET == 'iOS' ? CI_PATHS.macProjectRoot :
                                        params.BUILD_TARGET == 'Windows' ? CI_PATHS.windowsProjectRoot :
                                                params.BUILD_TARGET == 'OneStore' ? CI_PATHS.oneStoreProjectRoot :
                                                        CI_PATHS.androidProjectRoot

                        def buildMethods = [
                                'GooglePlay': 'Ayve.Editor.BuildEditor.BuildAndroid',
                                'OneStore'  : 'Ayve.Editor.BuildEditor.BuildOneStore',
                                'iOS'       : 'Ayve.Editor.BuildEditor.BuildiOS',
                                'Windows'   : 'Ayve.Editor.BuildEditor.BuildWindows'
                        ]

                        def buildMethod = buildMethods[params.BUILD_TARGET]

                        if (buildMethod == null) {
                            error "지원하지 않는 BUILD_TARGET 입니다: ${params.BUILD_TARGET}"
                        }

                        def unityBuildTarget = [
                                'GooglePlay': 'android',
                                'OneStore'  : 'android',
                                'iOS'       : 'ios',
                                'Windows'   : 'win64'
                        ][params.BUILD_TARGET]

                        echo '빌드커맨드 구성'

                        def iosBuildCommand = """#!/bin/bash
                            set -euxo pipefail

                            export VERSION_STRING=${params.VERSION_STRING}
                            export MOBILE_BUNDLE_CODE=${env.EFFECTIVE_MOBILE_BUNDLE_CODE}
                            export IsApk=${params.IsApk}
                            export ServerTarget=${params.TARGET_SERVER}
                            export IsOneStore=${params.BUILD_TARGET == 'OneStore'}

                            echo "---- Unity build env ----"
                            echo "JENKINS_PARAM_TARGET_SERVER=${params.TARGET_SERVER}"
                            echo "UNITY_ENV_ServerTarget=\$ServerTarget"
                            echo "UNITY_ENV_VERSION_STRING=\$VERSION_STRING"
                            echo "UNITY_ENV_MOBILE_BUNDLE_CODE=\$MOBILE_BUNDLE_CODE"
                            echo "UNITY_PROJECT_PATH=${projectPath}"

                            "${unityPath}" -batchmode -nographics -quit -projectPath "${projectPath}" -buildTarget ${
                            unityBuildTarget} -executeMethod ${buildMethod}
                        """

                        def windowsBuildCommand = """
                            SET VERSION_STRING=${params.VERSION_STRING}
                            SET MOBILE_BUNDLE_CODE=${env.EFFECTIVE_MOBILE_BUNDLE_CODE}
                            SET IsApk=${params.IsApk}
                            SET ServerTarget=${params.TARGET_SERVER}
                            SET IsOneStore=${params.BUILD_TARGET == 'OneStore'}

                            echo ---- Unity build env ----
                            echo JENKINS_PARAM_TARGET_SERVER=${params.TARGET_SERVER}
                            echo UNITY_ENV_ServerTarget=%ServerTarget%
                            echo UNITY_ENV_VERSION_STRING=%VERSION_STRING%
                            echo UNITY_ENV_MOBILE_BUNDLE_CODE=%MOBILE_BUNDLE_CODE%
                            echo UNITY_PROJECT_PATH=${projectPath}

                            "${unityPath}" -batchmode -nographics -quit -projectPath "${projectPath}" -buildTarget ${
                            unityBuildTarget} -executeMethod ${buildMethod}
                        """

                        echo '빌드커맨드 시작'

                        if (params.BUILD_TARGET == 'iOS') {
                            sh """#!/bin/bash
                                set -euxo pipefail

                                echo "---- Unity project before clean ----"
                                echo "JENKINS_PARAM_TARGET_SERVER=${params.TARGET_SERVER}"
                                echo "UNITY_PROJECT_PATH=${projectPath}"
                                pwd
                                ls -ld "${CI_PATHS.macProjectRoot}" || true
                                git -C "${projectPath}" rev-parse --show-toplevel
                                git -C "${projectPath}" rev-parse --abbrev-ref HEAD
                                git -C "${projectPath}" rev-parse HEAD

                                rm -rf "${projectPath}/Builds/iOS"
                            """

                            sh iosBuildCommand

                            sh """#!/bin/bash
                                set -euxo pipefail

                                echo "---- Unity output plist after BuildiOS ----"
                                PLIST="${projectPath}/Builds/iOS/Info.plist"
                                echo "PLIST=\$PLIST"
                                test -f "\$PLIST"

                                /usr/libexec/PlistBuddy -c "Print :CFBundleDisplayName" "\$PLIST" || true
                                /usr/libexec/PlistBuddy -c "Print :CFBundleName" "\$PLIST" || true
                                /usr/libexec/PlistBuddy -c "Print :CFBundleExecutable" "\$PLIST" || true
                                /usr/libexec/PlistBuddy -c "Print :CFBundleIdentifier" "\$PLIST" || true
                            """
                        } else if (params.BUILD_TARGET in ['GooglePlay', 'OneStore']) {
                            /*
                             * GooglePlay과 OneStore 병렬 빌드 시
                             * Gradle Daemon 및 캐시 경로가 겹치지 않도록 분리합니다.
                             */
                            def gradleUserHome =
                                    "${CI_PATHS.gradleUserHomeRoot}\\${params.BUILD_TARGET}"

                            echo """
==================================================
Android Gradle 환경 분리
BUILD_TARGET     : ${params.BUILD_TARGET}
GRADLE_USER_HOME : ${gradleUserHome}
==================================================
"""

                            withEnv([
                                    "GRADLE_USER_HOME=${gradleUserHome}"
                            ]) {
                                bat windowsBuildCommand
                            }
                        } else {
                            bat windowsBuildCommand
                        }
                    } catch (e) {
                        throw e
                    }
                }
            }

            post {
                failure {
                    script {
                        slackNotify("❌ ${params.BUILD_TARGET} Unity 빌드 실패")
                    }
                }
            }
        }

        stage('Android 결과물 저장') {
            when {
                beforeAgent true
                expression {
                    params.BUILD_TARGET in ['GooglePlay', 'OneStore']
                }
            }

            agent {
                label 'windows'
            }

            steps {
                script {
                    def versionDir = params.VERSION_STRING
                    def projectPath =
                            params.BUILD_TARGET == 'OneStore'
                                    ? CI_PATHS.oneStoreProjectRoot
                                    : CI_PATHS.androidProjectRoot

                    def source = "${projectPath}/Builds/Android"

                    def storeFolderName = params.BUILD_TARGET == 'GooglePlay' ? 'GooglePlay' : 'OneStore'
                    def dest = "${CI_PATHS.windowsOutputRoot}/${params.TARGET_SERVER}/${storeFolderName}/${versionDir}"
                    def patterns = '*.apk, *.aab'
                    def shouldStoreGooglePlaySymbols =
                            params.BUILD_TARGET == 'GooglePlay' && !params.IsApk
                    def symbolDest = shouldStoreGooglePlaySymbols
                            ? "${CI_PATHS.windowsSymbolsRoot}/GooglePlay/${versionDir}-v${env.EFFECTIVE_MOBILE_BUNDLE_CODE}"
                            : ''
                    def symbolPattern = shouldStoreGooglePlaySymbols
                            ? "*-${versionDir}-v${env.EFFECTIVE_MOBILE_BUNDLE_CODE}-*.symbols.zip"
                            : ''

                    powershell """
                        \$source = '${source}'
                        \$dest = '${dest}'
                        \$patterns = @(${patterns.split(',').collect {"'${it.trim()}'"}.join(', ')})
                        \$symbolDest = '${symbolDest}'
                        \$symbolPattern = '${symbolPattern}'

                        if (!(Test-Path \$dest)) {
                            New-Item -Path \$dest -ItemType Directory | Out-Null
                        }

                        foreach (\$pattern in \$patterns) {
                            Get-ChildItem -Path \$source -Include \$pattern -Recurse | ForEach-Object {
                                Move-Item -Path \$_.FullName -Destination \$dest -Force
                            }
                        }

                        if (![string]::IsNullOrWhiteSpace(\$symbolDest)) {
                            if (!(Test-Path -LiteralPath \$symbolDest)) {
                                New-Item -Path \$symbolDest -ItemType Directory -ErrorAction Stop | Out-Null
                            }

                            \$symbolFiles = @(
                                Get-ChildItem -LiteralPath \$source -Filter \$symbolPattern -File -Recurse -ErrorAction Stop
                            )

                            if (\$symbolFiles.Count -eq 0) {
                                throw "Google Play 심볼 파일을 찾을 수 없습니다: \$source/\$symbolPattern"
                            }

                            foreach (\$symbolFile in \$symbolFiles) {
                                Move-Item -LiteralPath \$symbolFile.FullName -Destination \$symbolDest -Force -ErrorAction Stop
                            }
                        }
                    """
                }
            }

            post {
                failure {
                    script {
                        slackNotify("❌ ${params.BUILD_TARGET} Android 결과물 저장 실패")
                    }
                }
            }
        }

        stage('Clean old iOS outputs') {
            when {
                beforeAgent true
                expression {
                    params.BUILD_TARGET == 'iOS' &&
                            ['ALPHA', 'DEV', 'CHEAT'].contains(params.TARGET_SERVER)
                }
            }

            agent {
                label 'mac'
            }

            steps {
                script {
                    def IOS_OUTPUT_ROOT = CI_PATHS.iosOutputRoot
                    def IOS_TARGET_ROOT = "${IOS_OUTPUT_ROOT}/iOS/${params.TARGET_SERVER}"

                    sh """#!/bin/bash
                        set +e

                        echo "Cleaning iOS outputs..."
                        echo "TARGET_ROOT=${IOS_TARGET_ROOT}"

                        du -sh "${IOS_TARGET_ROOT}" 2>/dev/null || true

                        if [ -d "${IOS_TARGET_ROOT}" ]; then
                          find "${IOS_TARGET_ROOT}" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
                        fi

                        mkdir -p "${IOS_TARGET_ROOT}"

                        du -sh "${IOS_TARGET_ROOT}" 2>/dev/null || true
                    """
                }
            }
        }

        stage('IOS Pod + Archive') {
            when {
                beforeAgent true
                expression {
                    params.BUILD_TARGET == 'iOS'
                }
            }

            agent {
                label 'mac'
            }

            steps {
                script {
                    def projectPath = CI_PATHS.macProjectRoot
                    def iosDir = "${projectPath}/Builds/iOS"
                    def IOS_OUTPUT_ROOT = CI_PATHS.iosOutputRoot
                    def outDir = "${IOS_OUTPUT_ROOT}/iOS/${params.TARGET_SERVER}/${params.VERSION_STRING}_${env.EFFECTIVE_MOBILE_BUNDLE_CODE}"
                    def archivePath = "${outDir}/archive.xcarchive"

                    sh """#!/bin/bash
                        rm -rf "${archivePath}"
                        set -euxo pipefail

                        echo "IOS_DIR=${iosDir}"

                        echo "ARCHIVE_PROJECT_PATH=${projectPath}"
                        ls -ld "${CI_PATHS.macProjectRoot}" || true
                        git -C "${projectPath}" rev-parse --show-toplevel || true
                        git -C "${projectPath}" rev-parse --abbrev-ref HEAD || true
                        git -C "${projectPath}" rev-parse HEAD || true

                        echo "OUT_DIR=${outDir}"
                        mkdir -p "${outDir}"

                        test -d "${iosDir}"
                        echo "---- IOS_DIR list ----"
                        ls -al "${iosDir}"

                        PLIST=""
                        if [ -f "${iosDir}/Info.plist" ]; then PLIST="${iosDir}/Info.plist"; fi
                        if [ -z "\$PLIST" ] && [ -f "${iosDir}/Unity-iPhone/Info.plist" ]; then PLIST="${iosDir}/Unity-iPhone/Info.plist"; fi
                        if [ -z "\$PLIST" ]; then
                          PLIST=\$(find "${iosDir}" -maxdepth 4 -type f -name Info.plist -print -quit 2>/dev/null || true)
                        fi
                        echo "PLIST=\$PLIST"

                        if [ -n "\$PLIST" ] && [ -f "\$PLIST" ]; then
                          echo "---- Xcode project plist before archive ----"
                          /usr/libexec/PlistBuddy -c "Print :CFBundleDisplayName" "\$PLIST" || true
                          /usr/libexec/PlistBuddy -c "Print :CFBundleName" "\$PLIST" || true
                          /usr/libexec/PlistBuddy -c "Print :CFBundleExecutable" "\$PLIST" || true
                          /usr/libexec/PlistBuddy -c "Print :CFBundleIdentifier" "\$PLIST" || true
                        fi

                        if [ -n "\$PLIST" ] && [ -f "\$PLIST" ]; then
                          /usr/libexec/PlistBuddy -c "Delete :NSAppTransportSecurity:NSAllowsArbitraryLoadsInWebContent" "\$PLIST" || true

                          /usr/libexec/PlistBuddy -c "Delete :ITSAppUsesNonExemptEncryption" "\$PLIST" 2>/dev/null || true
                          /usr/libexec/PlistBuddy -c "Add :ITSAppUsesNonExemptEncryption bool false" "\$PLIST"

                          /usr/libexec/PlistBuddy -c "Delete :UISupportedInterfaceOrientations" "\$PLIST" 2>/dev/null || true
                          /usr/libexec/PlistBuddy -c "Add :UISupportedInterfaceOrientations array" "\$PLIST"
                          /usr/libexec/PlistBuddy -c "Add :UISupportedInterfaceOrientations:0 string UIInterfaceOrientationLandscapeLeft" "\$PLIST"
                          /usr/libexec/PlistBuddy -c "Add :UISupportedInterfaceOrientations:1 string UIInterfaceOrientationLandscapeRight" "\$PLIST"

                          /usr/libexec/PlistBuddy -c "Delete :UISupportedInterfaceOrientations~ipad" "\$PLIST" 2>/dev/null || true
                          /usr/libexec/PlistBuddy -c "Add :UISupportedInterfaceOrientations~ipad array" "\$PLIST"
                          /usr/libexec/PlistBuddy -c "Add :UISupportedInterfaceOrientations~ipad:0 string UIInterfaceOrientationPortrait" "\$PLIST"
                          /usr/libexec/PlistBuddy -c "Add :UISupportedInterfaceOrientations~ipad:1 string UIInterfaceOrientationPortraitUpsideDown" "\$PLIST"
                          /usr/libexec/PlistBuddy -c "Add :UISupportedInterfaceOrientations~ipad:2 string UIInterfaceOrientationLandscapeLeft" "\$PLIST"
                          /usr/libexec/PlistBuddy -c "Add :UISupportedInterfaceOrientations~ipad:3 string UIInterfaceOrientationLandscapeRight" "\$PLIST"
                        fi

                        test -f "${iosDir}/Podfile"
                        cd "${iosDir}"
                        export LANG=en_US.UTF-8
                        export COCOAPODS_DISABLE_STATS=1

                        POD_BIN="${CI_PATHS.macPodExecutable}"
                        if [ ! -x "\$POD_BIN" ]; then POD_BIN="${CI_PATHS.macPodFallbackExecutable}"; fi
                        "\$POD_BIN" --version

                        "\$POD_BIN" install

                        XCODE_WORKSPACE="${iosDir}/Unity-iPhone.xcworkspace"
                        if [ ! -d "\$XCODE_WORKSPACE" ]; then
                          FOUND=\$(find "${iosDir}" -maxdepth 2 -type d -name "*.xcworkspace" -print -quit 2>/dev/null || true)
                          [ -n "\$FOUND" ] && XCODE_WORKSPACE="\$FOUND"
                        fi
                        echo "XCODE_WORKSPACE=\$XCODE_WORKSPACE"
                        test -d "\$XCODE_WORKSPACE"

                        xcodebuild archive \\
                          -workspace "\$XCODE_WORKSPACE" \\
                          -scheme "Unity-iPhone" \\
                          -configuration Release \\
                          -archivePath "${archivePath}" \\
                          -destination 'generic/platform=iOS' \\
                          -sdk iphoneos \\
                          DEVELOPMENT_TEAM=${APPLE_TEAM_ID} \\
                          CODE_SIGN_STYLE=Automatic \\
                          -allowProvisioningUpdates

                        test -d "${archivePath}"

                        ARCHIVE_PLIST=\$(find "${archivePath}/Products/Applications" -maxdepth 3 -name Info.plist -print -quit)
                        echo "ARCHIVE_PLIST=\$ARCHIVE_PLIST"
                        test -n "\$ARCHIVE_PLIST"

                        echo "---- Archived app plist ----"
                        /usr/libexec/PlistBuddy -c "Print :CFBundleDisplayName" "\$ARCHIVE_PLIST" || true
                        /usr/libexec/PlistBuddy -c "Print :CFBundleName" "\$ARCHIVE_PLIST" || true
                        /usr/libexec/PlistBuddy -c "Print :CFBundleExecutable" "\$ARCHIVE_PLIST" || true
                        /usr/libexec/PlistBuddy -c "Print :CFBundleIdentifier" "\$ARCHIVE_PLIST" || true
                    """
                }
            }

            post {
                failure {
                    script {
                        slackNotify('❌ IOS Pod + Archive세팅 실패')
                    }
                }
            }
        }

        stage('IOS Export Internal IPA') {
            when {
                beforeAgent true
                expression {
                    params.BUILD_TARGET == 'iOS' &&
                            ['INTERNAL_IPA', 'BOTH'].contains(params.IOS_DIST_MODE)
                }
            }

            agent {
                label 'mac'
            }

            steps {
                script {
                    def IOS_OUTPUT_ROOT = CI_PATHS.iosOutputRoot
                    def outDir = "${IOS_OUTPUT_ROOT}/iOS/${params.TARGET_SERVER}/${params.VERSION_STRING}_${env.EFFECTIVE_MOBILE_BUNDLE_CODE}"
                    def archivePath = "${outDir}/archive.xcarchive"
                    def exportPath = "${outDir}/Export"
                    def exportPlist = "${outDir}/ExportOptions_${params.TARGET_SERVER}.plist"
                    def iconFileName = (params.BRANCH_NAME == 'AppleBuild') ? 'PokerMade_icon.png' : 'PokerMade_icon_512.png'

                    sh """#!/bin/bash
                        rm -rf "${exportPath}"
                        set -euxo pipefail

                        cat > "${exportPlist}" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>compileBitcode</key><false/>
  <key>destination</key><string>export</string>

  <key>manifest</key>
  <dict>
    <key>appURL</key>
    <string>https://${CI_STORAGE.bucketName}.s3.${CI_STORAGE.awsRegion}.amazonaws.com/Client/Builds/${params.TARGET_SERVER}/iOS/${
                        params.VERSION_STRING}/PokerMade.ipa</string>
    <key>displayImageURL</key>
    <string>https://${CI_STORAGE.bucketName}.s3.${CI_STORAGE.awsRegion}.amazonaws.com/Client/Icon/${iconFileName}</string>
    <key>fullSizeImageURL</key>
    <string>https://${CI_STORAGE.bucketName}.s3.${CI_STORAGE.awsRegion}.amazonaws.com/Client/Icon/${iconFileName}</string>
  </dict>

  <key>method</key><string>release-testing</string>
  <key>signingStyle</key><string>automatic</string>
  <key>stripSwiftSymbols</key><true/>
  <key>teamID</key><string>${APPLE_TEAM_ID}</string>
  <key>thinning</key><string>&lt;none&gt;</string>
</dict>
</plist>
EOF

                        xcodebuild -exportArchive \\
                          -archivePath "${archivePath}" \\
                          -exportPath "${exportPath}" \\
                          -exportOptionsPlist "${exportPlist}" \\
                          -allowProvisioningUpdates

                        IPA_FILE=\$(find "${exportPath}" -maxdepth 2 -name "*.ipa" -print -quit)
                        test -n "\$IPA_FILE"

                        TARGET_IPA="${exportPath}/PokerMade.ipa"
                        if [ "\$IPA_FILE" != "\$TARGET_IPA" ]; then
                          rm -f "\$TARGET_IPA"
                          mv "\$IPA_FILE" "\$TARGET_IPA"
                        fi

                        test -f "\$TARGET_IPA"

                        MANIFEST_PLIST="${exportPath}/manifest.plist"
                        if [ -f "\$MANIFEST_PLIST" ]; then
                          /usr/libexec/PlistBuddy -c "Set :items:0:metadata:title PokerMade" "\$MANIFEST_PLIST"
                          /usr/libexec/PlistBuddy -c "Print :items:0:metadata:title" "\$MANIFEST_PLIST"
                        fi

                        find "${exportPath}" -maxdepth 2 -name "*.ipa" -print
                    """
                }
            }

            post {
                failure {
                    script {
                        slackNotify('❌ IOS Export Internal IPA 실패')
                    }
                }
            }
        }

        stage('IOS Upload to TestFlight Internal Only') {
            when {
                beforeAgent true
                expression {
                    params.BUILD_TARGET == 'iOS' &&
                            ['TESTFLIGHT_INTERNAL_ONLY'].contains(params.IOS_DIST_MODE)
                }
            }

            agent {
                label 'mac'
            }

            steps {
                withCredentials([
                        file(credentialsId: CI_CREDENTIALS.ascApiKeyFile, variable: 'ASC_KEY_FILE'),
                        string(credentialsId: CI_CREDENTIALS.ascApiKeyId, variable: 'ASC_KEY_ID'),
                        string(credentialsId: CI_CREDENTIALS.ascIssuerId, variable: 'ASC_ISSUER_ID')
                ]) {
                    script {
                        def IOS_OUTPUT_ROOT = CI_PATHS.iosOutputRoot
                        def outDir = "${IOS_OUTPUT_ROOT}/iOS/${params.TARGET_SERVER}/${params.VERSION_STRING}_${env.EFFECTIVE_MOBILE_BUNDLE_CODE}"
                        def archivePath = "${outDir}/archive.xcarchive"
                        def tfExportPath = "${outDir}/TestFlightInternalOnly"
                        def tfPlist = "${outDir}/ExportOptions_TestFlightInternalOnly.plist"

                        sh """#!/bin/bash
                            set -euxo pipefail
                            rm -rf "${tfExportPath}"

                            cat > "${tfPlist}" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>destination</key><string>upload</string>
  <key>method</key><string>app-store-connect</string>
  <key>signingStyle</key><string>automatic</string>
  <key>teamID</key><string>${APPLE_TEAM_ID}</string>
  <key>uploadSymbols</key><true/>
  <key>testFlightInternalTestingOnly</key><true/>
</dict>
</plist>
EOF

                            xcodebuild -exportArchive \\
                              -archivePath "${archivePath}" \\
                              -exportPath "${tfExportPath}" \\
                              -exportOptionsPlist "${tfPlist}" \\
                              -authenticationKeyPath "\$ASC_KEY_FILE" \\
                              -authenticationKeyID "\$ASC_KEY_ID" \\
                              -authenticationKeyIssuerID "\$ASC_ISSUER_ID" \\
                              -allowProvisioningUpdates
                        """
                    }
                }
            }

            post {
                failure {
                    script {
                        slackNotify('❌ iOS TestFlight Internal Only 업로드 실패')
                    }
                }
            }
        }

        stage('Window Upload to S3') {
            when {
                beforeAgent true
                expression {
                    params.BUILD_TARGET == 'Windows'
                }
            }

            agent {
                label 'windows'
            }

            steps {
                withCredentials([[$class: 'AmazonWebServicesCredentialsBinding', credentialsId: "${params.AWS_CRED_ID}"]]) {
                    script {
                        String localDir = "${CI_PATHS.windowsOutputRoot}\\${params.TARGET_SERVER}\\Windows\\${params.VERSION_STRING}"
                        def s3Dest = "${S3_Bucket}/Launcher/${params.TARGET_SERVER}/${params.VERSION_STRING}/"

                        echo "로컬 번들 경로: ${localDir}"
                        echo "S3 대상 경로 : ${s3Dest}"

                        bat """aws s3 sync "${localDir}" "${s3Dest}" --delete"""
                    }
                }
            }

            post {
                failure {
                    script {
                        slackNotify("❌ ${params.BUILD_TARGET} Window Upload to S3 실패")
                    }
                }
            }
        }

        stage('IOS Upload to S3') {
            when {
                beforeAgent true
                expression {
                    params.BUILD_TARGET == 'iOS' &&
                            ['INTERNAL_IPA', 'BOTH'].contains(params.IOS_DIST_MODE)
                }
            }

            agent {
                label 'mac'
            }

            steps {
                withCredentials([[$class: 'AmazonWebServicesCredentialsBinding', credentialsId: "${params.AWS_CRED_ID}"]]) {
                    script {
                        sh """
                            "${CI_PATHS.macAwsExecutable}" s3 sync ${CI_PATHS.iosOutputRoot}/iOS/${params.TARGET_SERVER}/${
                            params.VERSION_STRING}_${env.EFFECTIVE_MOBILE_BUNDLE_CODE
                        }/Export/ ${S3_Bucket}/Builds/${params.TARGET_SERVER}/${params.BUILD_TARGET}/${
                            params.VERSION_STRING}/ --delete
                        """
                    }
                }
            }

            post {
                failure {
                    script {
                        slackNotify("❌ ${params.BUILD_TARGET} IOS Upload to S3 실패")
                    }
                }
            }
        }

        stage('IOS Build Move to Window') {
            when {
                beforeAgent true
                expression {
                    params.BUILD_TARGET == 'iOS' &&
                            ['INTERNAL_IPA', 'BOTH'].contains(params.IOS_DIST_MODE)
                }
            }

            agent {
                label 'windows'
            }

            steps {
                withCredentials([[$class: 'AmazonWebServicesCredentialsBinding', credentialsId: "${params.AWS_CRED_ID}"]]) {
                    script {
                        bat """
                            aws s3 sync ${S3_Bucket}/Builds/${params.TARGET_SERVER}/${
                            params.BUILD_TARGET}/${params.VERSION_STRING}/ ${CI_PATHS.windowsOutputRoot}\\${
                            params.TARGET_SERVER}\\iOS\\${params.VERSION_STRING}\\ --delete
                        """
                    }
                }
            }

            post {
                failure {
                    script {
                        slackNotify("❌ ${params.BUILD_TARGET} IOS Build Move to Window 실패")
                    }
                }
            }
        }

        stage('IOS Upload to App Store Connect') {
            when {
                beforeAgent true
                expression {
                    params.BUILD_TARGET == 'iOS' &&
                            ['APPSTORE_UPLOAD', 'BOTH'].contains(params.IOS_DIST_MODE)
                }
            }

            agent {
                label 'mac'
            }

            steps {
                withCredentials([
                        file(credentialsId: CI_CREDENTIALS.ascApiKeyFile, variable: 'ASC_KEY_FILE'),
                        string(credentialsId: CI_CREDENTIALS.ascApiKeyId, variable: 'ASC_KEY_ID'),
                        string(credentialsId: CI_CREDENTIALS.ascIssuerId, variable: 'ASC_ISSUER_ID')
                ]) {
                    script {
                        def IOS_OUTPUT_ROOT = CI_PATHS.iosOutputRoot
                        def outDir = "${IOS_OUTPUT_ROOT}/iOS/${params.TARGET_SERVER}/${params.VERSION_STRING}_${env.EFFECTIVE_MOBILE_BUNDLE_CODE}"
                        def archivePath = "${outDir}/archive.xcarchive"
                        def appStoreExportPath = "${outDir}/AppStoreUpload"
                        def appStorePlist = "${outDir}/ExportOptions_AppStore.plist"

                        sh """#!/bin/bash
                            rm -rf "${appStoreExportPath}"
                            set -euxo pipefail

                            cat > "${appStorePlist}" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>destination</key><string>upload</string>
  <key>method</key><string>app-store-connect</string>
  <key>signingStyle</key><string>automatic</string>
  <key>teamID</key><string>${APPLE_TEAM_ID}</string>
  <key>uploadSymbols</key><true/>
</dict>
</plist>
EOF

                            xcodebuild -exportArchive \\
                              -archivePath "${archivePath}" \\
                              -exportPath "${appStoreExportPath}" \\
                              -exportOptionsPlist "${appStorePlist}" \\
                              -authenticationKeyPath "\$ASC_KEY_FILE" \\
                              -authenticationKeyID "\$ASC_KEY_ID" \\
                              -authenticationKeyIssuerID "\$ASC_ISSUER_ID" \\
                              -allowProvisioningUpdates
                        """
                    }
                }
            }

            post {
                failure {
                    script {
                        slackNotify('❌ iOS App Store Connect 업로드 실패')
                    }
                }
            }
        }

        stage('Upload Mobile Bundle Code') {
            when {
                beforeAgent true
                expression {
                    params.AUTO_MOBILE_BUNDLE_CODE &&
                            params.TARGET_SERVER == 'LIVE' &&
                            (
                                    (params.BUILD_TARGET == 'GooglePlay' && !params.IsApk) ||
                                            (params.BUILD_TARGET == 'OneStore')
                            )
                }
            }

            agent {
                label 'windows'
            }

            steps {
                withCredentials([[$class: 'AmazonWebServicesCredentialsBinding', credentialsId: "${params.AWS_CRED_ID}"]]) {
                    script {
                        writeFile file: 'VersionCode', text: env.EFFECTIVE_MOBILE_BUNDLE_CODE

                        bat """
                            aws s3 cp VersionCode ${env.MOBILE_BUNDLE_CODE_S3_PATH} --quiet
                        """

                        echo "MOBILE_BUNDLE_CODE 자동 증가 확정: ${env.PREV_MOBILE_BUNDLE_CODE} -> ${env.EFFECTIVE_MOBILE_BUNDLE_CODE}"
                    }
                }
            }

            post {
                failure {
                    script {
                        slackNotify('❌ MOBILE_BUNDLE_CODE S3 업로드 실패')
                    }
                }
            }
        }

        stage('AssetBundleBuild') {
            when {
                beforeAgent true
                expression {
                    params.IsAssetBundle
                }
            }

            agent none

            steps {
                script {
                    def projectNameMap = [
                            'Windows'   : 'casino (윈도우)',
                            'GooglePlay': 'PokerMade_Android (안드로이드)',
                            'OneStore'  : 'PokerMade_OneStore (안드로이드)',
                            'iOS'       : 'Casino (iOS)'
                    ]

                    def marketChannelMap = [
                            'Windows'   : 'GooglePlay',
                            'GooglePlay': 'GooglePlay',
                            'OneStore'  : 'OneStore',
                            'iOS'       : 'GooglePlay'
                    ]

                    def downstreamParams = [
                            string(
                                    name: 'BUNDLE_TYPE',
                                    value:
                                            params.BUILD_TARGET == 'Windows'
                                                    ? 'StandaloneWindows64'
                                                    : params.BUILD_TARGET == 'iOS'
                                                    ? 'iOS'
                                                    : 'Android'
                            ),
                            string(
                                    name: 'MARKET_CHANNEL',
                                    value: marketChannelMap[params.BUILD_TARGET]
                            ),
                            string(
                                    name: 'BUILD_OPTION',
                                    value: 'None'
                            ),
                            string(
                                    name: 'PROJECT_NAME',
                                    value: projectNameMap[params.BUILD_TARGET]
                            ),
                            string(
                                    name: 'Version',
                                    value: params.VERSION_STRING
                            ),
                            string(
                                    name: 'BRANCH_NAME',
                                    value: params.BRANCH_NAME
                            ),
                            string(
                                    name: 'TARGET_SERVER',
                                    value: params.TARGET_SERVER
                            ),
                            booleanParam(
                                    name: 'SLACK_ENABLED',
                                    value: params.SLACK_ENABLED
                            )
                    ]

                    echo """
==================================================
AssetBundleBuild 실행 시작
BUILD_TARGET : ${params.BUILD_TARGET}
VERSION      : ${params.VERSION_STRING}
SERVER       : ${params.TARGET_SERVER}
PROJECT_NAME : ${projectNameMap[params.BUILD_TARGET]}
MARKET       : ${marketChannelMap[params.BUILD_TARGET]}
==================================================
"""

                    def assetBundleResult = build(
                            job: 'AssetBundleBuild',
                            parameters: downstreamParams,
                            wait: true,
                            propagate: false
                    )

                    echo """
==================================================
AssetBundleBuild 실행 완료
BUILD_TARGET : ${params.BUILD_TARGET}
RESULT       : ${assetBundleResult.result}
URL          : ${assetBundleResult.absoluteUrl}
==================================================
"""

                    if (assetBundleResult.result != 'SUCCESS') {
                        error(
                                "${params.BUILD_TARGET} AssetBundleBuild 실패: " +
                                        "${assetBundleResult.result}"
                        )
                    }
                }
            }

            post {
                failure {
                    script {
                        def lbl = params.BUILD_TARGET == 'iOS'
                                ? 'mac'
                                : 'windows'

                        node(lbl) {
                            slackNotify(
                                    "❌ ${params.BUILD_TARGET} AssetBundleBuild 실패"
                            )
                        }
                    }
                }
            }
        }
    }

    post {
        success {
            script {
                def lbl = params.BUILD_TARGET == 'iOS'
                        ? 'mac'
                        : 'windows'

                def successType = params.IsAssetBundle
                        ? '앱 빌드 및 에셋번들 성공'
                        : '앱 빌드 성공'

                node(lbl) {
                    slackNotify(
                            "🎉 ${successType} - " +
                                    "${params.BUILD_TARGET} " +
                                    "${params.TARGET_SERVER} " +
                                    "${params.VERSION_STRING}"
                    )
                }
            }
        }

        failure {
            script {
                def lbl = params.BUILD_TARGET == 'iOS'
                        ? 'mac'
                        : 'windows'

                node(lbl) {
                    slackNotify(
                            '🔥 파이프라인 전체 실패 - ' +
                                    "${params.BUILD_TARGET} " +
                                    "${params.TARGET_SERVER} " +
                                    "${params.VERSION_STRING} 빌드"
                    )
                }
            }
        }

        aborted {
            script {
                def lbl = params.BUILD_TARGET == 'iOS'
                        ? 'mac'
                        : 'windows'

                node(lbl) {
                    slackNotify(
                            '⏹️ 파이프라인 중단 - ' +
                                    "${params.BUILD_TARGET} " +
                                    "${params.TARGET_SERVER} " +
                                    "${params.VERSION_STRING} 빌드"
                    )
                }
            }
        }
    }
}

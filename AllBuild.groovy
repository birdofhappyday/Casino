pipeline {
    agent none

    options {
        timestamps()

        // 이 상위 파이프라인 자체가 중복 실행되는 것만 방지
        disableConcurrentBuilds()
    }

    parameters {
        string(
                name: 'VERSION_STRING',
                defaultValue: '1.0.0',
                description: '메이저.마이너.패치'
        )

        string(
                name: 'BRANCH_NAME',
                defaultValue: 'main',
                description: 'Git 브랜치'
        )

        booleanParam(
                name: 'AUTO_MOBILE_BUNDLE_CODE',
                defaultValue: false,
                description: 'GooglePlay AAB VersionCode 자동 증가'
        )

        string(
                name: 'MOBILE_BUNDLE_CODE',
                defaultValue: '1',
                description: 'Android/iOS Bundle Code'
        )

        choice(
                name: 'TARGET_SERVER',
                choices: ['ALPHA', 'LIVE', 'CHEAT', 'DEV', 'BETA'],
                description: '서버 환경'
        )

        choice(
                name: 'IOS_DIST_MODE',
                choices: [
                        'INTERNAL_IPA',
                        'TESTFLIGHT_INTERNAL_ONLY',
                        'APPSTORE_UPLOAD',
                        'BOTH'
                ],
                description: 'iOS 배포 방식'
        )

        booleanParam(
                name: 'GOOGLE_PLAY_IS_APK',
                defaultValue: true,
                description: 'true=APK, false=AAB'
        )

        booleanParam(
                name: 'IsAssetBundle',
                defaultValue: true,
                description: 'GooglePlay/OneStore 성공 후 플랫폼별 AssetBundleBuild 실행'
        )

        booleanParam(
                name: 'SLACK_ENABLED',
                defaultValue: true,
                description: 'Slack 알림 사용'
        )

        string(
                name: 'AWS_CRED_ID',
                defaultValue: '<AWS_CREDENTIAL_ID>',
                description: 'AWS Credentials ID (포트폴리오 공개용 마스킹 값)'
        )
    }

    stages {
        stage('Parallel AppBuild') {
            steps {
                script {
                    /*
                     * 하위 AppBuild 실행 함수
                     */
                    def runAppBuild = {
                        String targetName,
                        boolean isApk,
                        boolean runAssetBundle ->

                            echo """
==================================================
AppBuild 실행 시작
BUILD_TARGET : ${targetName}
VERSION      : ${params.VERSION_STRING}
SERVER       : ${params.TARGET_SERVER}
IS_APK       : ${isApk}
ASSET_BUNDLE : ${runAssetBundle}
==================================================
"""

                            def childBuild = build(
                                    job: 'AppBuild',
                                    wait: true,
                                    propagate: false,
                                    parameters: [
                                            string(
                                                    name: 'VERSION_STRING',
                                                    value: params.VERSION_STRING
                                            ),

                                            string(
                                                    name: 'BRANCH_NAME',
                                                    value: params.BRANCH_NAME
                                            ),

                                            booleanParam(
                                                    name: 'AUTO_MOBILE_BUNDLE_CODE',
                                                    value: params.AUTO_MOBILE_BUNDLE_CODE
                                            ),

                                            string(
                                                    name: 'MOBILE_BUNDLE_CODE',
                                                    value: params.MOBILE_BUNDLE_CODE
                                            ),

                                            booleanParam(
                                                    name: 'IsApk',
                                                    value: isApk
                                            ),

                                            string(
                                                    name: 'BUILD_TARGET',
                                                    value: targetName
                                            ),

                                            string(
                                                    name: 'IOS_DIST_MODE',
                                                    value: params.IOS_DIST_MODE
                                            ),

                                            string(
                                                    name: 'TARGET_SERVER',
                                                    value: params.TARGET_SERVER
                                            ),

                                            string(
                                                    name: 'AWS_CRED_ID',
                                                    value: params.AWS_CRED_ID
                                            ),

                                            booleanParam(
                                                    name: 'IsAssetBundle',
                                                    value: runAssetBundle
                                            ),

                                            booleanParam(
                                                    name: 'SLACK_ENABLED',
                                                    value: params.SLACK_ENABLED
                                            )
                                    ]
                            )

                            echo """
==================================================
AppBuild 실행 완료
BUILD_TARGET : ${targetName}
RESULT       : ${childBuild.result}
URL          : ${childBuild.absoluteUrl}
==================================================
"""

                            if (childBuild.result != 'SUCCESS') {
                                error """
${targetName} AppBuild 실패

RESULT : ${childBuild.result}
URL    : ${childBuild.absoluteUrl}
"""
                            }
                    }

                    /*
                     * 최상위 병렬 그룹
                     *
                     * 1. Android 빌드 완료 후 Windows 실행
                     * 2. iOS는 독립적으로 동시 실행
                     */
                    def buildGroups = [:]

                    buildGroups['Android and Windows'] = {
                        stage('Parallel Android') {
                            def androidBuilds = [:]

                            androidBuilds['GooglePlay'] = {
                                stage('GooglePlay') {
                                    catchError(
                                            buildResult: 'FAILURE',
                                            stageResult: 'FAILURE',
                                            catchInterruptions: false
                                    ) {
                                        runAppBuild(
                                                'GooglePlay',
                                                params.GOOGLE_PLAY_IS_APK,
                                                params.IsAssetBundle
                                        )
                                    }
                                }
                            }

                            androidBuilds['OneStore'] = {
                                stage('OneStore') {
                                    catchError(
                                            buildResult: 'FAILURE',
                                            stageResult: 'FAILURE',
                                            catchInterruptions: false
                                    ) {
                                        runAppBuild(
                                                'OneStore',
                                                true,
                                                params.IsAssetBundle
                                        )
                                    }
                                }
                            }

                            parallel androidBuilds
                        }

                        /*
                         * Android 성공 여부와 관계없이 실행
                         */
                        stage('Windows') {
                            runAppBuild(
                                    'Windows',
                                    true,
                                    params.IsAssetBundle
                            )
                        }
                    }

                    /*
                     * iOS는 Android/Windows 그룹과 별도로 동시 실행
                     */
                    buildGroups['iOS'] = {
                        stage('iOS') {
                            runAppBuild(
                                    'iOS',
                                    true,
                                    params.IsAssetBundle
                            )
                        }
                    }

                    /*
                     * Android/Windows 그룹과 iOS 그룹 동시 시작
                     */
                    parallel buildGroups

                    echo '모든 플랫폼 AppBuild가 성공했습니다.'
                }
            }
        }
    }

    post {
        success {
            echo '전체 빌드 성공'
        }

        failure {
            echo '전체 빌드 중 하나 이상 실패'
        }

        aborted {
            echo '전체 빌드 중단'
        }

        always {
            echo '전체 빌드 파이프라인 종료'
        }
    }
}
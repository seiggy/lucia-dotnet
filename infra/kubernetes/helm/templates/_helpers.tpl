{{/*
Lucia Helm Chart Helper Templates

This file contains reusable template functions used throughout the Helm charts.
Include this in other templates with: {{ include "lucia.functionName" . }}
*/}}

{{/*
Expand the name of the chart.
*/}}
{{- define "lucia.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Create a default fully qualified app name.
*/}}
{{- define "lucia.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{/*
Create chart name and version as used by the chart label.
*/}}
{{- define "lucia.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Common labels for all resources.
*/}}
{{- define "lucia.labels" -}}
helm.sh/chart: {{ include "lucia.chart" . }}
{{ include "lucia.selectorLabels" . }}
{{- if .Chart.AppVersion }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- end }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{/*
Selector labels for pod selection.
*/}}
{{- define "lucia.selectorLabels" -}}
app.kubernetes.io/name: {{ include "lucia.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{/*
Create the name of the service account to use.
*/}}
{{- define "lucia.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "lucia.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}

{{/*
Redis host for ConfigMap and connection strings.
Returns "hostname" or "hostname.namespace.svc.cluster.local" depending on context.
*/}}
{{- define "lucia.redis.host" -}}
{{- if .Values.redis.enabled }}
{{- printf "%s-redis" (include "lucia.fullname" .) }}
{{- else }}
{{- .Values.redis.externalHost | default "redis" }}
{{- end }}
{{- end }}

{{/*
Format environment variables from map.
Usage: {{ include "lucia.env" . }}
*/}}
{{- define "lucia.env" -}}
{{- range $key, $value := . }}
- name: {{ $key }}
  value: {{ $value | quote }}
{{- end }}
{{- end }}

{{/*
Format environment variables from ConfigMap.
*/}}
{{- define "lucia.envFromConfigMap" -}}
- configMapRef:
    name: {{ include "lucia.fullname" . }}
{{- end }}

{{/*
Format environment variables from Secret.
*/}}
{{- define "lucia.envFromSecret" -}}
- secretRef:
    name: {{ include "lucia.fullname" . }}
{{- end }}

{{/*
Labels for pod disruption budget.
*/}}
{{- define "lucia.podDisruptionBudgetLabels" -}}
{{- include "lucia.labels" . }}
{{- end }}

{{/*
Common pod annotations for checksum verification.
Used to trigger pod restart when ConfigMap or Secret changes.
*/}}
{{- define "lucia.podAnnotations" -}}
checksum/config: {{ include (print $.Template.BasePath "/configmap.yaml") . | sha256sum }}
checksum/secret: {{ include (print $.Template.BasePath "/secret.yaml") . | sha256sum }}
{{- end }}

{{/*
Pod security policy name.
*/}}
{{- define "lucia.pspName" -}}
{{- default (include "lucia.fullname" .) .Values.podSecurityPolicy.name }}
{{- end }}

{{/*
Image pull secrets.
*/}}
{{- define "lucia.imagePullSecrets" -}}
{{- if .Values.imagePullSecrets }}
imagePullSecrets:
{{- range .Values.imagePullSecrets }}
  - name: {{ . }}
{{- end }}
{{- end }}
{{- end }}

{{/*
Kubernetes version check for API compatibility.
*/}}
{{- define "lucia.kubeVersion" -}}
{{- .Capabilities.KubeVersion.GitVersion }}
{{- end }}

{{/*
Return true if Kubernetes API version is >= 1.24.
*/}}
{{- define "lucia.isKube124Plus" -}}
{{- if (semverCompare ">=1.24-0" (include "lucia.kubeVersion" .)) }}true{{ end }}
{{- end }}

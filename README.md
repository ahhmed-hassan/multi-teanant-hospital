# Hospital K8s Multi-Tenant Demo

## What This Project Demonstrates

A multi-tenant Kubernetes deployment simulating two isolated hospital environments 
on a local Kind cluster. The project covers Helm chart authoring, tenant isolation 
via taints, tolerations and node affinity, Network Policies, and health probes, 
with notes throughout on how each pattern maps to a production AKS deployment.

## Repository Structure
```
hospital-k8s/
├── cluster/             Kind cluster configuration
├── app/                 ASP.NET Core Web API + Dockerfile
├── charts/              Helm chart with per-tenant values files
├── namespaces/          Namespace definitions
└── network-policies/    Network isolation rules
```
## Local Setup

The following steps recreate the full environment from scratch. 
This is the exact sequence to run after cloning the repository 
or after deleting the Kind cluster.

**Prerequisites:** Kind, kubectl, Helm, and Docker installed locally.

1. Create the cluster

```bash
kind create cluster --config cluster/kind-config.yaml --name hospital-demo
```

2. Install Calico for Network Policy enforcement

```bash
kubectl apply -f https://raw.githubusercontent.com/projectcalico/calico/v3.27.0/manifests/calico.yaml

kubectl wait --namespace kube-system \
  --for=condition=ready pod \
  --selector=k8s-app=calico-node \
  --timeout=90s
```

3. Load the application image into Kind

```bash
kind load docker-image hospital-api:1.0 --name hospital-demo
```

4. Create namespaces and apply Network Policies

```bash
kubectl apply -f namespaces/hospital-1.yaml
kubectl apply -f namespaces/hospital-2.yaml
kubectl apply -f network-policies/deny-cross-namespace.yaml
```

5. Deploy both hospital releases

```bash
helm install hospital-1-api ./charts/hospital-api \
  --namespace hospital-1 \
  --values ./charts/hospital-api/values-hospital-1.yaml

helm install hospital-2-api ./charts/hospital-api \
  --namespace hospital-2 \
  --values ./charts/hospital-api/values-hospital-2.yaml
```

## Architecture Decisions

### Why node pool isolation instead of namespace-only

Namespace-only isolation is a logical boundary. It scopes RBAC and Network 
Policies but does not prevent two tenants from running on the same physical node. 
In a healthcare context, where patient data compliance requires stronger guarantees, 
sharing a node between hospitals introduces noisy-neighbour risk and is unlikely 
to satisfy audit requirements.

Each worker node in this cluster carries a NoExecute taint scoped to a specific 
hospital. Combined with matching tolerations and required node affinity in each 
Helm release, hospital workloads are guaranteed to land only on their designated 
node. No toleration means no scheduling, as demonstrated during testing when even 
a throwaway test pod could not schedule without the correct toleration.

On AKS this pattern maps to dedicated node pools per tenant, with node pool taints 
applied at creation time.

### Why Calico instead of kindnet

Kind ships with kindnet as its default CNI. kindnet does not enforce Network 
Policies. Applying a NetworkPolicy object with kindnet results in no error but also 
no enforcement. Traffic that should be blocked passes through silently.

Calico is installed here with the default CNI disabled so that Network Policies are 
actually enforced and verifiable. This maps directly to the AKS requirement of using 
Azure CNI with Azure Network Policy or Calico, rather than Kubenet, which has the 
same limitation as kindnet.

### Why default-deny with explicit allow

The Network Policy in this project denies all ingress to a namespace by default and 
then permits traffic from within the same namespace only. This is more secure than 
explicitly targeting cross-namespace traffic because it blocks any source not on the 
allow list, including namespaces added in the future. The allow rule references the 
namespace by name via the kubernetes.io/metadata.name label rather than relying on 
implicit same-namespace scoping, which makes the intent readable without knowledge 
of that implicit behaviour.

## Verification

After setup, the following checks confirm both node isolation and network isolation 
are working correctly.

**Node isolation**

```bash
kubectl get pod -n hospital-1 -o wide
kubectl get pod -n hospital-2 -o wide
```

hospital-1-api must show hospital-demo-worker as its node. 
hospital-2-api must show hospital-demo-worker2.

**Network isolation**

```bash
$overrides = '{"spec":{"tolerations":[{"key":"hospital","operator":"Equal","value":"hospital-1","effect":"NoExecute"}]}}'

kubectl run test-pod -n hospital-1 --restart=Never \
  --image=busybox \
  --overrides=$overrides \
  -- wget -O- -T 5 http://hospital-2-api.hospital-2.svc.cluster.local/health/live

kubectl logs test-pod -n hospital-1
```

DNS resolves the hospital-2 service name successfully. The TCP connection times out, 
confirming the Network Policy is enforced. Same-namespace traffic to hospital-1-api 
returns 200 Healthy.

## Production AKS Notes

**CNI choice is permanent.** Azure CNI versus Kubenet must be decided at cluster 
creation time and cannot be changed afterwards. Network Policies require Azure CNI 
with either Azure Network Policy or Calico. Choosing Kubenet and later discovering 
the need for Network Policies means rebuilding the cluster.

**Workload Identity replaces secrets for Azure resource access.** Rather than 
storing credentials in Kubernetes Secrets or environment variables, each workload 
gets a Kubernetes ServiceAccount linked to an Azure Managed Identity via a federated 
credential. Application code uses DefaultAzureCredential() with no changes from the 
local pattern. No credentials are stored in etcd.

**Secrets Store CSI Driver for Key Vault integration.** Kubernetes Secrets are 
base64-encoded, not encrypted. For patient data compliance, secrets such as 
connection strings and encryption keys should be stored in Azure Key Vault and 
mounted into Pods via the Secrets Store CSI Driver. The application sees a normal 
file at a known path. The secret never touches etcd.

## Parking Lot

The Network Policy YAML is currently duplicated per tenant and is a candidate 
for Helm templating once a third hospital namespace is added. The chart would 
accept the tenant name as a value and render the namespaceSelector dynamically.
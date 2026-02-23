import { useMemo, useCallback } from 'react'
import {
  ReactFlow,
  Background,
  type Node,
  type Edge,
  type NodeProps,
  Handle,
  Position,
  useNodesState,
  useEdgesState,
  MarkerType,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import type { MeshTopology, MeshNode as MeshNodeData } from '../types'

// ── Agent state colors (Observatory theme) ──

const STATE_COLORS: Record<string, { bg: string; border: string; text: string }> = {
  'Processing Prompt...':  { bg: '#c4903020', border: '#c49030', text: '#c49030' },
  'Calling Tools...':      { bg: '#3b82f620', border: '#60a5fa', text: '#60a5fa' },
  'Generating Response...': { bg: '#6b8f7120', border: '#6b8f71', text: '#6b8f71' },
  'Processing...':         { bg: '#c4903015', border: '#a07830', text: '#a07830' },
  'Error':                 { bg: '#ef444420', border: '#f87171', text: '#f87171' },
  'Idle':                  { bg: '#1e1e22', border: '#3a3a40', text: '#6b6b78' },
}

const IDLE_STYLE = STATE_COLORS['Idle']

export interface NodeState {
  state: string
  active: boolean
}

type MeshNodeType = Node<{
  label: string
  icon: string
  nodeType: string
  isRemote?: boolean
  nodeState: NodeState
}>

// ── Custom Node Component ──

function AgentNode({ data }: NodeProps<MeshNodeType>) {
  const s = (data.nodeState.state && STATE_COLORS[data.nodeState.state]) || IDLE_STYLE
  const isActive = data.nodeState.active

  return (
    <div
      className="relative flex items-center gap-2 rounded-xl border px-3 py-2 font-sans transition-all duration-300"
      style={{
        background: s.bg,
        borderColor: s.border,
        color: s.text,
        boxShadow: isActive ? `0 0 12px ${s.border}40` : 'none',
        minWidth: 120,
      }}
    >
      <Handle type="target" position={Position.Top} className="!bg-transparent !border-0 !w-3 !h-3" />
      <span className="text-lg">{data.icon}</span>
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-1 truncate text-sm font-medium" style={{ color: isActive ? s.text : '#d0d0d8' }}>
          {data.label}
          {data.isRemote && <span className="text-[10px]" title="Remote agent">🌐</span>}
        </div>
        {isActive && data.nodeState.state !== 'Idle' && (
          <div className="truncate text-[10px]" style={{ color: s.text }}>{data.nodeState.state}</div>
        )}
      </div>
      <Handle type="source" position={Position.Bottom} className="!bg-transparent !border-0 !w-3 !h-3" />
      {isActive && data.nodeState.state !== 'Error' && (
        <div
          className="absolute inset-0 rounded-xl animate-pulse pointer-events-none"
          style={{ border: `1px solid ${s.border}60` }}
        />
      )}
    </div>
  )
}

const nodeTypes = { agentNode: AgentNode }

// ── Layout helpers ──

function getIcon(nodeType: string): string {
  if (nodeType === 'orchestrator') return '🧠'
  if (nodeType === 'tool') return '🔧'
  return '🤖'
}

function layoutNodes(
  topologyNodes: MeshNodeData[],
  _topologyEdges: { source: string; target: string }[],
  nodeStates: Record<string, NodeState>,
): { nodes: MeshNodeType[]; edges: Edge[] } {
  const orchestrator = topologyNodes.find(n => n.nodeType === 'orchestrator')
  const agents = topologyNodes.filter(n => n.nodeType === 'agent')
  const tools = topologyNodes.filter(n => n.nodeType === 'tool')

  const nodes: MeshNodeType[] = []
  const edges: Edge[] = []

  const nodeSpacingX = 200
  const agentY = 160
  const toolY = 320

  // Orchestrator at center top
  if (orchestrator) {
    const cx = Math.max(agents.length - 1, 0) * nodeSpacingX / 2
    nodes.push({
      id: orchestrator.id,
      type: 'agentNode',
      position: { x: cx - 60, y: 0 },
      data: {
        label: orchestrator.label,
        icon: getIcon(orchestrator.nodeType),
        nodeType: orchestrator.nodeType,
        nodeState: nodeStates[orchestrator.id] || { state: 'Idle', active: false },
      },
    })
  }

  // Agents in a row
  agents.forEach((agent, i) => {
    nodes.push({
      id: agent.id,
      type: 'agentNode',
      position: { x: i * nodeSpacingX, y: agentY },
      data: {
        label: agent.label,
        icon: getIcon(agent.nodeType),
        nodeType: agent.nodeType,
        isRemote: agent.isRemote,
        nodeState: nodeStates[agent.id] || { state: 'Idle', active: false },
      },
    })

    // Edge from orchestrator
    if (orchestrator) {
      const ns = nodeStates[agent.id]
      const isActive = ns?.active
      edges.push({
        id: `e-${orchestrator.id}-${agent.id}`,
        source: orchestrator.id,
        target: agent.id,
        animated: isActive || false,
        style: {
          stroke: isActive ? '#c49030' : '#3a3a40',
          strokeWidth: isActive ? 2 : 1,
        },
        markerEnd: {
          type: MarkerType.ArrowClosed,
          color: isActive ? '#c49030' : '#3a3a40',
          width: 16,
          height: 16,
        },
      })
    }

    // Tools for this agent
    const agentTools = tools.filter(t => t.id.startsWith(`${agent.id}:`))
    agentTools.forEach((tool, ti) => {
      const toolX = i * nodeSpacingX + (ti - (agentTools.length - 1) / 2) * 150
      nodes.push({
        id: tool.id,
        type: 'agentNode',
        position: { x: toolX, y: toolY },
        data: {
          label: tool.label,
          icon: getIcon(tool.nodeType),
          nodeType: tool.nodeType,
          nodeState: nodeStates[tool.id] || { state: 'Idle', active: false },
        },
      })

      const toolNs = nodeStates[tool.id]
      const toolActive = toolNs?.active
      edges.push({
        id: `e-${agent.id}-${tool.id}`,
        source: agent.id,
        target: tool.id,
        animated: toolActive || false,
        style: {
          stroke: toolActive ? '#60a5fa' : '#3a3a40',
          strokeWidth: toolActive ? 2 : 1,
        },
        markerEnd: {
          type: MarkerType.ArrowClosed,
          color: toolActive ? '#60a5fa' : '#3a3a40',
          width: 14,
          height: 14,
        },
      })
    })
  })

  return { nodes, edges }
}

// ── Main Component ──

interface MeshGraphProps {
  topology: MeshTopology | null
  nodeStates: Record<string, NodeState>
}

export default function MeshGraph({ topology, nodeStates }: MeshGraphProps) {
  if (!topology || topology.nodes.length === 0) {
    return (
      <div className="flex h-64 items-center justify-center rounded-xl border border-stone bg-charcoal text-dust">
        No agents registered
      </div>
    )
  }

  const { nodes: layoutedNodes, edges: layoutedEdges } = useMemo(
    () => layoutNodes(topology.nodes, topology.edges, nodeStates),
    [topology, nodeStates],
  )

  const [nodes, setNodes, onNodesChange] = useNodesState(layoutedNodes)
  const [edges, setEdges, onEdgesChange] = useEdgesState(layoutedEdges)

  // Sync layout when topology or states change
  useMemo(() => {
    setNodes(layoutedNodes)
    setEdges(layoutedEdges)
  }, [layoutedNodes, layoutedEdges, setNodes, setEdges])

  const onInit = useCallback((instance: { fitView: () => void }) => {
    instance.fitView()
  }, [])

  return (
    <div className="h-[420px] rounded-xl border border-stone bg-charcoal overflow-hidden">
      <ReactFlow
        nodes={nodes}
        edges={edges}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onInit={onInit}
        nodeTypes={nodeTypes}
        fitView
        minZoom={0.4}
        maxZoom={1.5}
        proOptions={{ hideAttribution: true }}
        nodesDraggable={false}
        nodesConnectable={false}
        elementsSelectable={false}
        panOnDrag
        zoomOnScroll
      >
        <Background color="#3a3a4030" gap={20} />
      </ReactFlow>
    </div>
  )
}

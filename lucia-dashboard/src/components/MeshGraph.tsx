/**
 * Real-time agent mesh graph visualization using React Flow.
 *
 * Displays orchestrator, agent, and tool nodes connected by edges.
 * Node colours change based on the agent's current processing state
 * (idle, processing, calling tools, generating response, error).
 *
 * Receives topology from `/api/activity/mesh` and state updates
 * from the SSE {@link useActivityStream} hook in the parent page.
 */

import { useMemo, useCallback, useEffect } from 'react'
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
import { Bot, BrainCircuit, Globe2, Wrench } from 'lucide-react'

// ── Agent state colors (Observatory theme) ──

const STATE_COLORS: Record<string, { bg: string; border: string; text: string }> = {
  'Processing Prompt...':  { bg: 'color-mix(in srgb, var(--color-amber) 14%, transparent)', border: 'var(--color-amber-dim)', text: 'var(--color-amber)' },
  'Calling Tools...':      { bg: 'color-mix(in srgb, var(--color-info) 14%, transparent)', border: 'var(--color-info)', text: 'var(--color-info)' },
  'Generating Response...': { bg: 'color-mix(in srgb, var(--color-sage) 14%, transparent)', border: 'var(--color-sage-dim)', text: 'var(--color-sage)' },
  'Processing...':         { bg: 'color-mix(in srgb, var(--color-amber) 10%, transparent)', border: 'var(--color-amber-dim)', text: 'var(--color-amber)' },
  'Error':                 { bg: 'color-mix(in srgb, var(--color-ember) 12%, transparent)', border: 'var(--color-rose-dim)', text: 'var(--color-rose)' },
  'Idle':                  { bg: 'var(--color-basalt)', border: 'var(--color-stone)', text: 'var(--color-dust)' },
}

const IDLE_STYLE = STATE_COLORS['Idle']

/** Current processing state of an agent node in the mesh graph. */
export interface NodeState {
  state: string
  active: boolean
}

type MeshNodeType = Node<{
  label: string
  nodeType: string
  isRemote?: boolean
  nodeState: NodeState
}>

// ── Custom Node Component ──

function AgentNode({ data }: NodeProps<MeshNodeType>) {
  const s = (data.nodeState.state && STATE_COLORS[data.nodeState.state]) || IDLE_STYLE
  const isActive = data.nodeState.active
  const Icon = data.nodeType === 'orchestrator' ? BrainCircuit : data.nodeType === 'tool' ? Wrench : Bot

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
      <Icon className="h-4 w-4 shrink-0" aria-hidden="true" />
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-1 truncate text-sm font-medium" style={{ color: isActive ? s.text : 'var(--color-light)' }}>
          {data.label}
          {data.isRemote && <Globe2 className="h-3 w-3 shrink-0" aria-label="Remote agent" />}
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
          stroke: isActive ? 'var(--color-amber-dim)' : 'var(--color-ash)',
          strokeWidth: isActive ? 2 : 1,
        },
        markerEnd: {
          type: MarkerType.ArrowClosed,
          color: isActive ? 'var(--color-amber-dim)' : 'var(--color-ash)',
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
          stroke: toolActive ? 'var(--color-info)' : 'var(--color-ash)',
          strokeWidth: toolActive ? 2 : 1,
        },
        markerEnd: {
          type: MarkerType.ArrowClosed,
          color: toolActive ? 'var(--color-info)' : 'var(--color-ash)',
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
  const hasTopology = Boolean(topology && topology.nodes.length > 0)

  const { nodes: layoutedNodes, edges: layoutedEdges } = useMemo(
    () => {
      if (!hasTopology || !topology) {
        return { nodes: [], edges: [] }
      }
      return layoutNodes(topology.nodes, topology.edges, nodeStates)
    },
    [hasTopology, topology, nodeStates],
  )

  const [nodes, setNodes, onNodesChange] = useNodesState(layoutedNodes)
  const [edges, setEdges, onEdgesChange] = useEdgesState(layoutedEdges)

  // Sync layout when topology or states change
  useEffect(() => {
    setNodes(layoutedNodes)
    setEdges(layoutedEdges)
  }, [layoutedNodes, layoutedEdges, setNodes, setEdges])

  const onInit = useCallback((instance: { fitView: () => void }) => {
    instance.fitView()
  }, [])

  if (!hasTopology) {
    return (
      <div className="flex h-64 items-center justify-center rounded-xl border border-stone bg-charcoal text-dust">
        No agents registered
      </div>
    )
  }

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
        <Background color="color-mix(in srgb, var(--color-ash) 35%, transparent)" gap={20} />
      </ReactFlow>
    </div>
  )
}

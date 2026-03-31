export const SCENARIOS = [
  { id: 'demo_chat', name: 'Demo Chat', description: 'Simple echo chat for UI testing' },
  { id: 'ai_chat', name: 'AI Chat', description: 'Chat with AI (OpenAI)' },
  { id: 'onboarding', name: 'Onboarding', description: 'Short onboarding survey to tailor the system to you' },
  { id: 'routed_chat', name: 'Routed Chat', description: 'Router: weather forecast or onboarding survey (subgraphs)' },
  {
    id: 'supervisor_routed_chat',
    name: 'Supervisor Routed Chat',
    description: 'Task-stack supervisor: AI menu chooses continue/start/switch/cancel/resume actions',
  },
] as const

export type ScenarioId = (typeof SCENARIOS)[number]['id']

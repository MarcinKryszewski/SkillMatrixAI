import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { api } from './api'
import './App.css'

type User = {
  id: string
  email: string
  departmentId: number | null
  roles: string[]
}
type Department = { id: number; name: string }
type Machine = { id: number; name: string; description: string | null }
type Person = { id: string; email: string; departmentId: number; departmentName: string }
type Instruction = {
  id: number
  title: string
  departmentId: number
  departmentName: string
  machineId: number
  machineName: string
  skillLevel: number
  passingScore: number
  isPublished: boolean
  authorId: string
  questionCount: number
  updatedAt: string
}
type Matrix = {
  userId: string
  email: string
  machines: {
    id: number
    name: string
    levels: { level: number; passed: boolean }[]
    highestPassedLevel: number | null
  }[]
}
type Attempt = {
  id: number
  instructionId: number
  instructionTitle: string
  machineName: string
  skillLevel: number
  attemptNumber: number
  score: number
  passed: boolean
  completedAt: string
}
type QuestionDraft = {
  prompt: string
  choices: { text: string; isCorrect: boolean }[]
}
type InstructionDraft = {
  title: string
  content: string
  departmentId: number
  machineId: number
  skillLevel: number
  passingScore: number
  isPublished: boolean
  questions: QuestionDraft[]
}
type Test = {
  id: number
  title: string
  content: string
  skillLevel: number
  passingScore: number
  questions: {
    id: number
    prompt: string
    choices: { id: number; text: string }[]
  }[]
}
type RetakeRequest = {
  id: number
  status: string
  requestedAt: string
  userEmail: string
  instructionTitle: string
  machineName: string
  instructionId: number
}
type Tab = 'matrix' | 'tests' | 'results' | 'instructions' | 'manage' | 'retakes'

const emptyQuestion = (): QuestionDraft => ({
  prompt: '',
  choices: [
    { text: '', isCorrect: true },
    { text: '', isCorrect: false },
  ],
})
const emptyDraft = (): InstructionDraft => ({
  title: '',
  content: '',
  departmentId: 0,
  machineId: 0,
  skillLevel: 1,
  passingScore: 70,
  isPublished: true,
  questions: [emptyQuestion()],
})

function isStaff(user: User) {
  return user.roles.includes('Administrator') || user.roles.includes('Trainer')
}

function App() {
  const [user, setUser] = useState<User | null>(null)
  const [booting, setBooting] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [tab, setTab] = useState<Tab>('matrix')
  const [departments, setDepartments] = useState<Department[]>([])
  const [machines, setMachines] = useState<Machine[]>([])
  const [people, setPeople] = useState<Person[]>([])
  const [selectedPerson, setSelectedPerson] = useState('')
  const [matrix, setMatrix] = useState<Matrix | null>(null)
  const [instructions, setInstructions] = useState<Instruction[]>([])
  const [attempts, setAttempts] = useState<Attempt[]>([])
  const [retakes, setRetakes] = useState<RetakeRequest[]>([])
  const [activeTest, setActiveTest] = useState<Test | null>(null)
  const [answers, setAnswers] = useState<Record<number, number>>({})
  const [testResult, setTestResult] = useState<{ score: number; passed: boolean } | null>(null)
  const [draft, setDraft] = useState<InstructionDraft>(emptyDraft)
  const [editingId, setEditingId] = useState<number | null>(null)
  const [refresh, setRefresh] = useState(0)
  const [departmentName, setDepartmentName] = useState('')
  const [machineName, setMachineName] = useState('')
  const [newUser, setNewUser] = useState({
    email: '',
    password: '',
    role: 'Employee',
    departmentId: 0,
  })

  useEffect(() => {
    api<User>('/auth/me')
      .then(setUser)
      .catch(() => setUser(null))
      .finally(() => setBooting(false))
  }, [])

  useEffect(() => {
    if (!user) return
    let active = true
    const staff = isStaff(user)
    Promise.all([
      api<Department[]>('/departments'),
      api<Machine[]>('/machines'),
      api<Instruction[]>('/instructions'),
      staff ? api<Person[]>('/department/users') : Promise.resolve([]),
      user.roles.includes('Employee') ? api<Attempt[]>('/attempts') : Promise.resolve([]),
      staff ? api<RetakeRequest[]>('/retake-requests?status=Pending') : Promise.resolve([]),
    ])
      .then(([nextDepartments, nextMachines, nextInstructions, nextPeople, nextAttempts, nextRetakes]) => {
        if (!active) return
        setDepartments(nextDepartments)
        setMachines(nextMachines)
        setInstructions(nextInstructions)
        setPeople(nextPeople)
        setAttempts(nextAttempts)
        setRetakes(nextRetakes)
        if (nextDepartments.length > 0) {
          setNewUser((current) => current.departmentId
            ? current
            : { ...current, departmentId: nextDepartments[0].id })
        }
        setDraft((current) => ({
          ...current,
          departmentId: current.departmentId || user.departmentId || nextDepartments[0]?.id || 0,
          machineId: current.machineId || nextMachines[0]?.id || 0,
        }))
        if (staff && nextPeople[0]) {
          setSelectedPerson((current) => current || nextPeople[0].id)
        }
      })
      .catch((cause: unknown) => {
        if (active) setError(cause instanceof Error ? cause.message : 'Nie udało się pobrać danych.')
      })
    return () => {
      active = false
    }
  }, [user, refresh])

  useEffect(() => {
    if (!user) return
    const personId = isStaff(user) ? selectedPerson : user.id
    if (!personId) return
    api<Matrix>(`/skills?userId=${encodeURIComponent(personId)}`)
      .then(setMatrix)
      .catch((cause: unknown) => setError(cause instanceof Error ? cause.message : 'Nie udało się pobrać matrycy.'))
  }, [user, selectedPerson, refresh])

  const employeeInstructions = useMemo(
    () => instructions.filter((instruction) => instruction.isPublished),
    [instructions],
  )
  const visibleMatrix = user && isStaff(user) && !selectedPerson ? null : matrix

  async function handleLogin(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    setError('')
    try {
      const loggedIn = await api<User>('/auth/login', {
        method: 'POST',
        body: JSON.stringify({ email: form.get('email'), password: form.get('password') }),
      })
      setUser(loggedIn)
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Logowanie nie powiodło się.')
    }
  }

  async function logout() {
    try {
      await api('/auth/logout', { method: 'POST' })
      setUser(null)
      setMatrix(null)
      setNotice('')
      setError('')
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Nie udało się wylogować.')
    }
  }

  async function startTest(instructionId: number) {
    setError('')
    setNotice('')
    setTestResult(null)
    setAnswers({})
    try {
      setActiveTest(await api<Test>(`/instructions/${instructionId}/test`))
      setTab('tests')
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Nie udało się otworzyć testu.')
    }
  }

  async function submitTest(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!activeTest) return
    try {
      const result = await api<{ score: number; passed: boolean }>(
        `/instructions/${activeTest.id}/attempts`,
        { method: 'POST', body: JSON.stringify({ answers }) },
      )
      setTestResult(result)
      setNotice(result.passed ? 'Test zaliczony.' : 'Test niezaliczony. Możesz poprosić autora o zgodę na kolejne podejście.')
      setRefresh((value) => value + 1)
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Nie udało się zapisać wyniku.')
    }
  }

  async function requestRetake(instructionId: number) {
    try {
      await api(`/instructions/${instructionId}/retake-requests`, { method: 'POST', body: '{}' })
      setNotice('Prośba o ponowne podejście została wysłana do autora instrukcji.')
      setError('')
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Nie udało się wysłać prośby.')
    }
  }

  async function reviewRetake(id: number, approve: boolean) {
    try {
      await api(`/retake-requests/${id}/review`, {
        method: 'POST',
        body: JSON.stringify({ approve }),
      })
      setNotice(approve ? 'Zezwolono na ponowne podejście.' : 'Odrzucono prośbę.')
      setRefresh((value) => value + 1)
      setError('')
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Nie udało się rozpatrzyć prośby.')
    }
  }

  function updateQuestion(questionIndex: number, update: (question: QuestionDraft) => QuestionDraft) {
    setDraft((current) => ({
      ...current,
      questions: current.questions.map((question, index) =>
        index === questionIndex ? update(question) : question,
      ),
    }))
  }

  async function saveInstruction(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    try {
      const path = editingId ? `/instructions/${editingId}` : '/instructions'
      await api(path, {
        method: editingId ? 'PUT' : 'POST',
        body: JSON.stringify(draft),
      })
      setNotice(editingId ? 'Instrukcja została zaktualizowana.' : 'Instrukcja została utworzona.')
      setError('')
      setDraft(emptyDraft())
      setEditingId(null)
      setRefresh((value) => value + 1)
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Nie udało się zapisać instrukcji.')
    }
  }

  async function editInstruction(id: number) {
    try {
      const value = await api<InstructionDraft & { id: number; questions: QuestionDraft[] }>(
        `/instructions/${id}/edit`,
      )
      setEditingId(id)
      setDraft({ ...value, questions: value.questions.length ? value.questions : [emptyQuestion()] })
      setTab('instructions')
      setNotice('')
      setError('')
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Nie udało się pobrać instrukcji.')
    }
  }

  async function createDepartment(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    try {
      await api('/departments', { method: 'POST', body: JSON.stringify({ name: departmentName }) })
      setDepartmentName('')
      setRefresh((value) => value + 1)
      setNotice('Dodano dział.')
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Nie udało się dodać działu.')
    }
  }

  async function createMachine(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    try {
      await api('/machines', {
        method: 'POST',
        body: JSON.stringify({ name: machineName, description: '' }),
      })
      setMachineName('')
      setRefresh((value) => value + 1)
      setNotice('Dodano maszynę.')
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Nie udało się dodać maszyny.')
    }
  }

  async function createUser(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    try {
      await api('/users', { method: 'POST', body: JSON.stringify(newUser) })
      setNewUser((current) => ({ ...current, email: '', password: '' }))
      setRefresh((value) => value + 1)
      setNotice('Utworzono konto użytkownika.')
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Nie udało się utworzyć użytkownika.')
    }
  }

  if (booting) return <main className="loading">Ładowanie aplikacji…</main>
  if (!user) {
    return (
      <main className="login-shell">
        <section className="login-card">
          <div className="brand-mark">SM</div>
          <p className="eyebrow">Skill Matrix</p>
          <h1>Witaj w centrum rozwoju umiejętności</h1>
          <p className="muted">Zaloguj się, aby zobaczyć swoją matrycę i dostępne instrukcje.</p>
          {error && <div className="alert error">{error}</div>}
          <form onSubmit={handleLogin} className="stack">
            <label>
              Adres e-mail
              <input name="email" type="email" autoComplete="username" required />
            </label>
            <label>
              Hasło
              <input name="password" type="password" autoComplete="current-password" required />
            </label>
            <button className="primary" type="submit">Zaloguj się</button>
          </form>
        </section>
      </main>
    )
  }

  const admin = user.roles.includes('Administrator')
  const employee = user.roles.includes('Employee')
  const staff = isStaff(user)
  const navItems: { id: Tab; label: string }[] = [
    { id: 'matrix', label: 'Matryca umiejętności' },
    ...(employee
      ? [
          { id: 'tests' as const, label: 'Dostępne testy' },
          { id: 'results' as const, label: 'Moje wyniki' },
        ]
      : []),
    ...(staff
      ? [
          { id: 'instructions' as const, label: 'Instrukcje i testy' },
          { id: 'retakes' as const, label: 'Prośby o powtórkę' },
        ]
      : []),
    ...(admin ? [{ id: 'manage' as const, label: 'Zarządzanie' }] : []),
  ]

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <div className="brand-mark">SM</div>
          <div><strong>Skill Matrix</strong><span>Rozwój kompetencji</span></div>
        </div>
        <div className="side-label">MENU GŁÓWNE</div>
        <nav>
          {navItems.map((item) => (
            <button key={item.id} className={tab === item.id ? 'nav-item active' : 'nav-item'}
              onClick={() => { setTab(item.id); setActiveTest(null); setError(''); setNotice('') }}>
              <span className="nav-dot" />{item.label}
            </button>
          ))}
        </nav>
        <div className="sidebar-bottom">
          <div className="avatar">{user.email.slice(0, 1).toUpperCase()}</div>
          <div className="account"><strong>{user.email}</strong><span>{user.roles.join(', ')}</span></div>
          <button className="logout" aria-label="Wyloguj" onClick={logout}>↗</button>
        </div>
      </aside>

      <main className="main-content">
        <header className="topbar">
          <div>
            <span className="breadcrumb">Skill Matrix <span>/</span> {navItems.find((item) => item.id === tab)?.label}</span>
            <h1>{tab === 'matrix' ? 'Matryca umiejętności' : navItems.find((item) => item.id === tab)?.label}</h1>
          </div>
          <div className="topbar-meta"><span className="status-dot" /> System aktywny</div>
        </header>
        <section className="page">
          {error && <div className="alert error">{error}<button onClick={() => setError('')}>×</button></div>}
          {notice && <div className="alert success">{notice}<button onClick={() => setNotice('')}>×</button></div>}
          {tab === 'matrix' && (
            <div className="stack">
              <div className="intro-row">
                <div><p className="eyebrow">PRZEGLĄD KOMPETENCJI</p><h2>Poziomy według maszyn</h2>
                  <p className="muted">Zaliczone testy potwierdzają poziom umiejętności.</p></div>
                {staff && people.length > 0 && (
                  <label className="person-picker">Osoba
                    <select value={selectedPerson} onChange={(event) => setSelectedPerson(event.target.value)}>
                      {people.map((person) => <option key={person.id} value={person.id}>{person.email} · {person.departmentName}</option>)}
                    </select>
                  </label>
                )}
              </div>
              <div className="panel matrix-panel">
                <div className="panel-heading"><div><h3>{visibleMatrix?.email ?? 'Matryca'}</h3>
                  <p className="muted">Poziomy 1–5, oddzielnie dla każdej maszyny</p></div>
                  <span className="badge">{visibleMatrix?.machines.length ?? 0} maszyn</span></div>
                {visibleMatrix?.machines.length ? (
                  <div className="table-scroll"><table><thead><tr><th>Maszyna</th>
                    {[1, 2, 3, 4, 5].map((level) => <th key={level}>Poziom {level}</th>)}
                    <th>Najwyższy poziom</th></tr></thead><tbody>
                    {visibleMatrix.machines.map((machine) => <tr key={machine.id}><td className="machine-name">{machine.name}</td>
                      {machine.levels.map((level) => <td key={level.level}><span className={level.passed ? 'level-pill passed' : 'level-pill'}>{level.passed ? 'Zaliczony' : '—'}</span></td>)}
                      <td><strong>{machine.highestPassedLevel ? `${machine.highestPassedLevel} / 5` : '—'}</strong></td></tr>)}
                  </tbody></table></div>
                ) : <div className="empty-state">Brak maszyn lub wyników do wyświetlenia.</div>}
              </div>
              {employee && <InstructionCards instructions={employeeInstructions} onStart={startTest} />}
            </div>
          )}
          {tab === 'tests' && employee && (
            activeTest ? (
              <section className="panel test-panel">
                <button className="text-button" onClick={() => { setActiveTest(null); setTestResult(null) }}>← Wróć do listy testów</button>
                <p className="eyebrow">POZIOM {activeTest.skillLevel} · PRÓG {activeTest.passingScore}%</p>
                <h2>{activeTest.title}</h2><p className="instruction-content">{activeTest.content}</p>
                {testResult ? <div className={testResult.passed ? 'result-card passed-result' : 'result-card failed-result'}>
                  <span>Twój wynik</span><strong>{testResult.score}%</strong>
                  <p>{testResult.passed ? 'Gratulacje, test zaliczony.' : `Do zaliczenia potrzeba ${activeTest.passingScore}%.`}</p>
                  {!testResult.passed && <button className="secondary" onClick={() => requestRetake(activeTest.id)}>Poproś o zgodę na powtórkę</button>}
                </div> : <form onSubmit={submitTest} className="stack">
                  {activeTest.questions.map((question, index) => <fieldset className="question-card" key={question.id}>
                    <legend>{String(index + 1).padStart(2, '0')} · {question.prompt}</legend>
                    {question.choices.map((choice) => <label className="choice" key={choice.id}>
                      <input type="radio" name={`question-${question.id}`} required
                        checked={answers[question.id] === choice.id}
                        onChange={() => setAnswers((current) => ({ ...current, [question.id]: choice.id }))} />
                      {choice.text}
                    </label>)}
                  </fieldset>)}
                  <button className="primary" type="submit">Zakończ test</button>
                </form>}
              </section>
            ) : <InstructionCards instructions={employeeInstructions} onStart={startTest} />
          )}
          {tab === 'results' && employee && (
            <section className="panel"><div className="panel-heading"><div><h2>Historia wyników</h2>
              <p className="muted">Twoje podejścia i uzyskane rezultaty</p></div></div>
              {attempts.length ? <div className="table-scroll"><table><thead><tr><th>Test</th><th>Maszyna</th><th>Poziom</th><th>Podejście</th><th>Wynik</th><th>Data</th></tr></thead>
                <tbody>{attempts.map((attempt) => <tr key={attempt.id}><td>{attempt.instructionTitle}</td><td>{attempt.machineName}</td>
                  <td>{attempt.skillLevel}</td><td>{attempt.attemptNumber}</td><td><span className={attempt.passed ? 'level-pill passed' : 'level-pill'}>{attempt.score}% · {attempt.passed ? 'zaliczony' : 'niezaliczony'}</span></td>
                  <td>{new Date(attempt.completedAt).toLocaleDateString('pl-PL')}</td></tr>)}</tbody></table></div>
                : <div className="empty-state">Nie masz jeszcze wyników testów.</div>}
            </section>
          )}
          {tab === 'instructions' && staff && (
            <div className="content-grid">
              <section className="panel instruction-list"><div className="panel-heading"><div><h2>Twoje instrukcje</h2><p className="muted">Twórz i aktualizuj materiały oraz testy</p></div>
                <button className="secondary" onClick={() => { setEditingId(null); setDraft(emptyDraft()) }}>＋ Nowa instrukcja</button></div>
                {instructions.map((instruction) => <article className="instruction-row" key={instruction.id}>
                  <div><strong>{instruction.title}</strong><span>{instruction.machineName} · poziom {instruction.skillLevel} · {instruction.departmentName}</span></div>
                  <div className="row-actions"><span className={instruction.isPublished ? 'badge green' : 'badge'}>{instruction.isPublished ? 'Opublikowana' : 'Szkic'}</span>
                    {(admin || instruction.authorId === user.id) && <button className="text-button" onClick={() => editInstruction(instruction.id)}>Edytuj</button>}</div>
                </article>)}
              </section>
              <section className="panel editor-panel"><div className="panel-heading"><div><p className="eyebrow">EDYTOR</p><h2>{editingId ? 'Aktualizuj instrukcję' : 'Nowa instrukcja'}</h2></div></div>
                <form className="stack" onSubmit={saveInstruction}>
                  <label>Tytuł<input required value={draft.title} onChange={(event) => setDraft({ ...draft, title: event.target.value })} /></label>
                  <div className="form-grid">
                    <label>Dział<select required value={draft.departmentId} onChange={(event) => setDraft({ ...draft, departmentId: Number(event.target.value) })}>
                      {departments.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
                    <label>Maszyna<select required value={draft.machineId} onChange={(event) => setDraft({ ...draft, machineId: Number(event.target.value) })}>
                      {machines.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
                    <label>Poziom umiejętności<select value={draft.skillLevel} onChange={(event) => setDraft({ ...draft, skillLevel: Number(event.target.value) })}>
                      {[1, 2, 3, 4, 5].map((level) => <option key={level} value={level}>{level}</option>)}</select></label>
                    <label>Próg zaliczenia (%)<input type="number" min="1" max="100" required value={draft.passingScore} onChange={(event) => setDraft({ ...draft, passingScore: Number(event.target.value) })} /></label>
                  </div>
                  <label>Treść instrukcji<textarea rows={6} required value={draft.content} onChange={(event) => setDraft({ ...draft, content: event.target.value })} /></label>
                  <div className="editor-subhead"><div><h3>Pytania testowe</h3><span className="muted">Wybierz jedną prawidłową odpowiedź dla każdego pytania.</span></div>
                    <button className="text-button" type="button" onClick={() => setDraft({ ...draft, questions: [...draft.questions, emptyQuestion()] })}>＋ Dodaj pytanie</button></div>
                  {draft.questions.map((question, questionIndex) => <div className="question-editor" key={questionIndex}>
                    <div className="question-editor-title"><strong>Pytanie {questionIndex + 1}</strong>
                      {draft.questions.length > 1 && <button type="button" className="text-button danger-text" onClick={() => setDraft({ ...draft, questions: draft.questions.filter((_, index) => index !== questionIndex) })}>Usuń</button>}</div>
                    <label>Treść pytania<textarea rows={2} required value={question.prompt}
                      onChange={(event) => updateQuestion(questionIndex, (value) => ({ ...value, prompt: event.target.value }))} /></label>
                    {question.choices.map((choice, choiceIndex) => <div className="choice-editor" key={choiceIndex}>
                      <input aria-label={`Odpowiedź ${choiceIndex + 1}`} required value={choice.text} placeholder={`Odpowiedź ${choiceIndex + 1}`}
                        onChange={(event) => updateQuestion(questionIndex, (value) => ({ ...value, choices: value.choices.map((item, index) => index === choiceIndex ? { ...item, text: event.target.value } : item) }))} />
                      <label className="correct-choice"><input type="radio" name={`correct-${questionIndex}`} checked={choice.isCorrect}
                        onChange={() => updateQuestion(questionIndex, (value) => ({ ...value, choices: value.choices.map((item, index) => ({ ...item, isCorrect: index === choiceIndex })) }))} /> Poprawna</label>
                      {question.choices.length > 2 && <button className="text-button danger-text" type="button" onClick={() => updateQuestion(questionIndex, (value) => {
                        const choices = value.choices.filter((_, index) => index !== choiceIndex)
                        if (!choices.some((item) => item.isCorrect)) choices[0].isCorrect = true
                        return { ...value, choices }
                      })}>×</button>}
                    </div>)}
                    <button className="text-button" type="button" onClick={() => updateQuestion(questionIndex, (value) => ({ ...value, choices: [...value.choices, { text: '', isCorrect: false }] }))}>＋ Dodaj odpowiedź</button>
                  </div>)}
                  <label className="toggle"><input type="checkbox" checked={draft.isPublished} onChange={(event) => setDraft({ ...draft, isPublished: event.target.checked })} /> Opublikuj instrukcję dla działu</label>
                  <div className="form-actions"><button className="primary" type="submit">Zapisz instrukcję i test</button>
                    {editingId && <button type="button" className="secondary" onClick={() => { setEditingId(null); setDraft(emptyDraft()) }}>Anuluj edycję</button>}</div>
                </form>
              </section>
            </div>
          )}
          {tab === 'retakes' && staff && <section className="panel"><div className="panel-heading"><div><h2>Prośby o ponowne podejście</h2><p className="muted">Decyzja autora odblokuje jedno kolejne podejście.</p></div></div>
            {retakes.length ? retakes.map((request) => <article className="retake-row" key={request.id}>
              <div><strong>{request.userEmail}</strong><span>{request.instructionTitle} · {request.machineName} · {new Date(request.requestedAt).toLocaleDateString('pl-PL')}</span></div>
              <div className="row-actions"><button className="secondary" onClick={() => reviewRetake(request.id, false)}>Odrzuć</button><button className="primary compact" onClick={() => reviewRetake(request.id, true)}>Zezwól</button></div>
            </article>) : <div className="empty-state">Brak oczekujących próśb.</div>}
          </section>}
          {tab === 'manage' && admin && <div className="manage-grid">
            <section className="panel"><p className="eyebrow">STRUKTURA</p><h2>Działy</h2><form className="inline-form" onSubmit={createDepartment}>
              <input required placeholder="Nazwa działu" value={departmentName} onChange={(event) => setDepartmentName(event.target.value)} /><button className="primary compact">Dodaj</button></form>
              <div className="simple-list">{departments.map((item) => <span key={item.id}>{item.name}</span>)}</div></section>
            <section className="panel"><p className="eyebrow">URZĄDZENIA</p><h2>Maszyny</h2><form className="inline-form" onSubmit={createMachine}>
              <input required placeholder="Nazwa maszyny" value={machineName} onChange={(event) => setMachineName(event.target.value)} /><button className="primary compact">Dodaj</button></form>
              <div className="simple-list">{machines.map((item) => <span key={item.id}>{item.name}</span>)}</div></section>
            <section className="panel user-create"><p className="eyebrow">KONTA</p><h2>Dodaj użytkownika</h2><form className="stack" onSubmit={createUser}>
              <label>E-mail<input type="email" required value={newUser.email} onChange={(event) => setNewUser({ ...newUser, email: event.target.value })} /></label>
              <label>Hasło początkowe<input type="password" minLength={10} required value={newUser.password} onChange={(event) => setNewUser({ ...newUser, password: event.target.value })} /></label>
              <div className="form-grid"><label>Rola<select value={newUser.role} onChange={(event) => setNewUser({ ...newUser, role: event.target.value })}>
                <option value="Employee">Pracownik</option><option value="Trainer">Trener</option><option value="Administrator">Administrator</option></select></label>
                {newUser.role !== 'Administrator' && <label>Dział<select required value={newUser.departmentId} onChange={(event) => setNewUser({ ...newUser, departmentId: Number(event.target.value) })}>
                  {departments.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>}</div>
              <button className="primary">Utwórz konto</button></form>
            </section>
          </div>}
        </section>
      </main>
    </div>
  )
}

function InstructionCards({ instructions, onStart }: { instructions: Instruction[]; onStart: (id: number) => void }) {
  const [machineFilter, setMachineFilter] = useState('all')
  const [levelFilter, setLevelFilter] = useState('all')
  const visible = instructions.filter((instruction) =>
    (machineFilter === 'all' || instruction.machineId === Number(machineFilter))
    && (levelFilter === 'all' || instruction.skillLevel === Number(levelFilter)),
  )
  const machineOptions = [...new Map(instructions.map((item) => [item.machineId, item.machineName])).entries()]
  return (
    <section className="panel"><div className="panel-heading"><div><h2>Instrukcje i testy</h2><p className="muted">Wybierz maszynę i poziom, aby rozpocząć naukę.</p></div>
      <div className="filters"><select aria-label="Filtr maszyny" value={machineFilter} onChange={(event) => setMachineFilter(event.target.value)}>
        <option value="all">Wszystkie maszyny</option>{machineOptions.map(([id, name]) => <option key={id} value={id}>{name}</option>)}</select>
        <select aria-label="Filtr poziomu" value={levelFilter} onChange={(event) => setLevelFilter(event.target.value)}><option value="all">Wszystkie poziomy</option>
          {[1, 2, 3, 4, 5].map((level) => <option key={level} value={level}>Poziom {level}</option>)}</select></div></div>
      {visible.length ? <div className="instruction-grid">{visible.map((instruction) => <article className="learning-card" key={instruction.id}>
        <div className="card-kicker"><span className="badge">Poziom {instruction.skillLevel}</span><span className="muted">{instruction.machineName}</span></div>
        <h3>{instruction.title}</h3><p>{instruction.questionCount} pytań · próg {instruction.passingScore}%</p>
        <button className="secondary" onClick={() => onStart(instruction.id)}>Otwórz instrukcję i test <span>→</span></button>
      </article>)}</div> : <div className="empty-state">Brak instrukcji zgodnych z wybranymi filtrami.</div>}
    </section>
  )
}

export default App

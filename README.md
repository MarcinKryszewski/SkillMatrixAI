# Skill Matrix

Aplikacja do zarządzania matrycą umiejętności, instrukcjami stanowiskowymi i testami pracowników.

## Technologie

- ASP.NET Core 10, ASP.NET Core Identity, Entity Framework Core i SQLite
- React, TypeScript i Vite
- Logowanie oparte na cookie; role oraz dostęp ograniczany do działu

## Uruchomienie lokalne

Wymagania: .NET 10 SDK i Node.js 20.19+.

Uruchom API z katalogu repozytorium. Ustaw dane początkowego administratora; hasło musi mieć co najmniej 10 znaków, wielką i małą literę, cyfrę oraz znak specjalny.

```bash
SEED_ADMIN_EMAIL=admin@example.com \
SEED_ADMIN_PASSWORD='Zmien-to-mocne-haslo1!' \
dotnet run --project SkillMatrix.Api --urls http://localhost:5000
```

W drugim terminalu:

```bash
cd client
npm install
npm run dev
```

Otwórz adres Vite wyświetlony w terminalu (zwykle `http://localhost:5173`). Vite przekazuje żądania `/api` do backendu. Baza SQLite powstaje w `SkillMatrix.Api/App_Data/skillmatrix.db`.

Administrator tworzy działy, maszyny oraz konta trenerów i pracowników. Trener może tworzyć instrukcje i testy w swoim dziale oraz rozpatrywać prośby o powtórkę testów, które opracował. Pracownik widzi opublikowane instrukcje i może wykonywać wyłącznie testy własnego działu. Zaliczone testy aktualizują matrycę umiejętności osoby dla danej maszyny.

Konto może mieć jednocześnie dowolne role: Administrator, Trener i Pracownik; uprawnienia się sumują. Role Trenera lub Pracownika wymagają przypisanego działu, także w połączeniu z rolą Administratora. Administrator może zmieniać role i dział istniejących kont; zmiana unieważnia bieżącą sesję użytkownika.

## Konfiguracja

- `ConnectionStrings__Default` — connection string SQLite; domyślnie lokalna baza w `App_Data`.
- `SEED_ADMIN_EMAIL` i `SEED_ADMIN_PASSWORD` — ustaw oba, aby przy pierwszym uruchomieniu utworzyć konto administratora.
- `AllowedOrigins` — opcjonalna lista dozwolonych adresów frontendu; domyślnie `http://localhost:5173`.

Żądania modyfikujące API wymagają tokenu antiforgery pobranego z `/api/auth/csrf`; frontend obsługuje go automatycznie.

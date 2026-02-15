# National Walkability API

## Running the app and accessing the dashboard

1. **Prerequisites:** .NET SDK, MySQL (or MariaDB).
2. **Configure:** Copy `appsettings.example.json` to `appsettings.json` and set your database connection string. Create the database and tables (see below).
3. **Run:** From the project folder run `dotnet run`.
4. **Open in browser:** Go to **http://localhost:5000** (or the port shown in the console). Use the link on the home page to open the **National Walkability Dashboard**, or go directly to **http://localhost:5000/dashboard.html**.

If you deploy the app to a server, share that URL so others can access the dashboard.

## Getting the data (after cloning)

The repo does **not** include the database or data. To run with data:

1. **Create the database:** Run `Scripts/schema.sql` against your MySQL instance (creates `alabama_walkability` and tables).
2. **Configure:** Set `ConnectionStrings:Default` in `appsettings.json` (your server, user, password). Do not commit real credentials.
3. **Import current data:** Set `Import:CsvUrl` in appsettings to the EPA CSV URL below, or call `GET /api/import/csv?url=...` with that URL. The scheduled job or a one-off run will populate `block_groups` and `counties`.
4. **2010 data for trend forecast:** Run `Scripts/migration_add_block_groups_2010.sql`, then `POST /api/import/csv/file-2010` with the NCI CSV file (form field `file`).

All data comes from public EPA and NCI URLs.

## Data (CSV)

- **Current (EPA):** [EPA Smart Location Database CSV](https://edg.epa.gov/EPADataCommons/public/OA/EPA_SmartLocationDatabase_V3_Jan_2021_Final.csv)  
  Set `Import:CsvUrl` in appsettings to this URL.
- **2010 (NCI) for trend forecast:** Get the CSV from [NCI GIS research files](https://gis.cancer.gov/research/files.html) (e.g. Walk Index US block groups), then `POST /api/import/csv/file-2010` (form: `file`).
- when creating database in MySQL dont limit number of rows
  

## API

- **Dashboard:** `/`, `/dashboard.html`
- **State stats:** `GET /api/stats/state`
- **State forecast (10 yr):** `GET /api/stats/state-forecast?years=10`
- **Recommendations:** `GET /api/stats/state-recommendations`
- **Import from URL:** `GET /api/import/csv?url=<csv-url>`
- **Import 2010 from file:** `POST /api/import/csv/file-2010` (form: `file` = CSV)

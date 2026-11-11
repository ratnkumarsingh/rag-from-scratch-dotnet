-- Executed automatically the first time the Postgres container creates its database.
-- Creating the extension up-front means the .NET app already knows the "vector" type
-- the very first time it connects.
CREATE EXTENSION IF NOT EXISTS vector;

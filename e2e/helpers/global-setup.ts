import * as dotenv from 'dotenv'
import * as path from 'path'

async function globalSetup() {
  // Ensure env vars are loaded
  dotenv.config({ path: path.resolve(__dirname, '../.env') })
}

export default globalSetup
